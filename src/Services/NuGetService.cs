using System.IO;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

internal static class NuGetService
{
    private static bool _shutdownInitiated;

    private static NuGetSession _session = new();

    public static void Shutdown()
    {
        _shutdownInitiated = true;
        _session.Dispose();
    }

    public static void ClearCache()
    {
        if (_shutdownInitiated)
            return;

        Interlocked.Exchange(ref _session, new NuGetSession()).Dispose();
    }

    public static async Task<ICollection<PackageInfo>> CheckPackageReferences(IReadOnlyCollection<PackageReferenceEntry>? packageReferences)
    {
        var session = _session;

        session.ThrowIfCancellationRequested();

        var identitiesById = packageReferences
            .Select(item => item.Identity)
            .GroupBy(item => item.Id);

        var getPackageInfoTasks = identitiesById.Select(item => GetPackageInfo(item, session));

        var result = await Task.WhenAll(getPackageInfoTasks);

        session.ThrowIfCancellationRequested();

        return result
            .ExceptNullItems()
            .ToArray();
    }

    public static async Task<Package?> GetPackage(string packageId)
    {
        var session = _session;

        var package = await GetPackageCacheEntry(packageId, session).GetValue();

        session.ThrowIfCancellationRequested();

        return package;
    }

    public static async Task<PackageInfo?> GetPackageInfo(PackageIdentity packageIdentity)
    {
        var session = _session;

        var packageInfo = await GetPackageInfoCacheEntry(packageIdentity, session).GetValue();

        session.ThrowIfCancellationRequested();

        return packageInfo;
    }

    public static async Task<ICollection<PackageInfo>> GetTransitivePackages(IReadOnlyCollection<PackageReferenceEntry> packageReferences, ICollection<PackageInfo> topLevelPackages)
    {
        var inputQueue = new HashSet<PackageInfo>(topLevelPackages);

        var dependencyMap = new Dictionary<PackageIdentity, HashSet<PackageIdentity>>();

        var processedItems = new Dictionary<string, PackageInfo>();

        var projects = packageReferences
            .Select(reference => reference.ProjectItem.Project)
            .Distinct();

        // Limit scan to the superset of all target frameworks, to avoid too many false positives.
        var targetFrameworks = GetTargetFrameworks(projects);

        bool ShouldSkip(PackageIdentity identity)
        {
            if (!processedItems.TryGetValue(identity.Id, out var existing))
                return false;

            return existing.PackageIdentity.Version >= identity.Version;
        }

        while (inputQueue.FirstOrDefault() is { } currentItem)
        {
            inputQueue.Remove(currentItem);

            var packageIdentity = currentItem.PackageIdentity;

            if (ShouldSkip(packageIdentity))
                continue;

            processedItems[packageIdentity.Id] = currentItem;

            var (_, _, sourceRepository, session) = currentItem.Package;

            var dependencyIds = await GetDirectDependencies(packageIdentity, targetFrameworks, sourceRepository, session).ConfigureAwait(true);

            var packageDependencies = dependencyMap.ForceValue(packageIdentity, _ => new HashSet<PackageIdentity>());

            foreach (var dependencyId in dependencyIds)
            {
                packageDependencies.Add(dependencyId);

                if (ShouldSkip(dependencyId))
                    continue;

                if (await GetPackageInfoCacheEntry(dependencyId, session).GetValue() is not { } info)
                    continue;

                inputQueue.Add(info);
            }

            session.ThrowIfCancellationRequested();
        }

        var packages = processedItems.Values;

        foreach (var package in packages)
        {
            var dependencyTasks = dependencyMap[package.PackageIdentity].Select(item => GetPackageInfoCacheEntry(item, package.Package.Session).GetValue());

            var dependencies = await Task.WhenAll(dependencyTasks);

            package.Dependencies = dependencies.ExceptNullItems().ToArray();
        }

        foreach (var item in packages)
        {
            foreach (var dependency in item.Dependencies)
            {
                dependency.DependsOn.Add(item);
            }
        }

        var transitivePackages = packages.Except(topLevelPackages).ToArray();

        return transitivePackages;
    }

    private static NuGetFramework[] GetTargetFrameworks(IEnumerable<Project> projects)
    {
        var frameworkNames = projects.Select(project => project.GetProperty("TargetFrameworks") ?? project.GetProperty("TargetFramework"))
            .Select(item => item?.EvaluatedValue)
            .ExceptNullItems()
            .SelectMany(item => item.Split(';').Select(value => value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var frameworks = frameworkNames
            .Select(NuGetFramework.Parse)
            .ToArray();

        return frameworks;
    }

    private sealed record FrameworkSpecific(NuGetFramework TargetFramework) : IFrameworkSpecific;

    private static async Task<IReadOnlyCollection<PackageIdentity>> GetDirectDependencies(PackageIdentity packageIdentity, IEnumerable<NuGetFramework> targetFrameworks, SourceRepository repository, NuGetSession session)
    {
        // Don't scan packages with pseudo-references, they don't get physically included, but cause vulnerability warnings.
        if (string.Equals(packageIdentity.Id, "NETStandard.Library", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<PackageIdentity>();

        var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

        var packageStream = new MemoryStream();
        await resource
            .CopyNupkgToStreamAsync(packageIdentity.Id, packageIdentity.Version, packageStream, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken)
            ;

        if (packageStream.Length == 0)
            return Array.Empty<PackageIdentity>();

        packageStream.Position = 0;

        using var package = new PackageArchiveReader(packageStream);

        var dependencyGroups = package.GetPackageDependencies().ToArray();

        var frameworksInPackage = dependencyGroups.Select(group => new FrameworkSpecific(group.TargetFramework));

        var usedFrameworks = targetFrameworks
            .Select(framework => NuGetFrameworkUtility.GetNearest(frameworksInPackage, framework)?.TargetFramework)
            .ExceptNullItems()
            .ToHashSet();

        var dependencyIds = dependencyGroups
            .Where(group => usedFrameworks.Contains(group.TargetFramework))
            .SelectMany(dependencyGroup => dependencyGroup.Packages)
            .Select(dependency => new PackageIdentity(dependency.Id,
                dependency.VersionRange.MaxVersion ?? dependency.VersionRange.MinVersion))
            .Distinct()
            .ToArray();

        return dependencyIds;
    }

    private static async Task<PackageInfo?> GetPackageInfo(IEnumerable<PackageIdentity> packageIdentities, NuGetSession session)
    {
        // if multiple version are provided, use the oldest reference with the smallest version
        var packageIdentity = packageIdentities.OrderBy(item => item.Version.Version).First();

        return await GetPackageInfoCacheEntry(packageIdentity, session).GetValue();
    }

    private static PackageCacheEntry GetPackageCacheEntry(string packageId, NuGetSession session)
    {
        PackageCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            return new PackageCacheEntry(packageId, session);
        }

        lock (session)
        {
            return session.Cache.GetOrCreate(packageId, Factory) ??
                   throw new InvalidOperationException("Failed to get package from cache");
        }
    }

    private static PackageInfoCacheEntry GetPackageInfoCacheEntry(PackageIdentity packageIdentity, NuGetSession session)
    {
        PackageInfoCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);

            return new PackageInfoCacheEntry(packageIdentity, session);
        }

        lock (session)
        {
            return session.Cache.GetOrCreate(packageIdentity, Factory) ??
                   throw new InvalidOperationException("Failed to get package from cache");
        }
    }

    private static bool IsOutdated(PackageIdentity packageIdentity, IEnumerable<NuGetVersion> versions)
    {
        var latestVersion =
            versions.FirstOrDefault(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease);

        return latestVersion > packageIdentity.Version;
    }

    private abstract class CacheEntry<T> where T : class
    {
        private readonly TaskCompletionSource<T?> _taskCompletionSource = new();

        protected CacheEntry(Func<Task<T?>> generator)
        {
            Load(generator);
        }

        public Task<T?> GetValue()
        {
            return _taskCompletionSource.Task;
        }

        private async void Load(Func<Task<T?>> generator)
        {
            try
            {
                var value = await generator();

                _taskCompletionSource.SetResult(value);
            }
            catch
            {
                _taskCompletionSource.SetResult(default);
            }
        }
    }

    private sealed class PackageCacheEntry : CacheEntry<Package>
    {
        public PackageCacheEntry(string packageId, NuGetSession session)
            : base(() => GetPackage(packageId, session))
        {
        }

        private static async Task<Package?> GetPackage(string packageId, NuGetSession session)
        {
            foreach (var sourceRepository in await session.GetSourceRepositories())
            {
                var packageResource = await sourceRepository
                    .GetResourceAsync<FindPackageByIdResource>(session.CancellationToken)
                    ;

                var unsortedVersions = await packageResource
                    .GetAllVersionsAsync(packageId, session.SourceCacheContext, NullLogger.Instance,
                        session.CancellationToken)
                    ;

                var versions = unsortedVersions?.OrderByDescending(item => item).ToArray();

                if (versions?.Length > 0)
                {
                    return new Package(packageId, versions, sourceRepository, session);
                }
            }

            return null;
        }
    }

    private sealed class PackageInfoCacheEntry : CacheEntry<PackageInfo>
    {
        public PackageInfoCacheEntry(PackageIdentity packageIdentity, NuGetSession session)
            : base(() => GetPackageInfo(packageIdentity, session))
        {
        }

        private static async Task<PackageInfo?> GetPackageInfo(PackageIdentity packageIdentity, NuGetSession session)
        {
            var package = await GetPackageCacheEntry(packageIdentity.Id, session).GetValue();
            if (package is null)
                return null;

            var sourceRepository = package.SourceRepository;

            var packageMetadataResource = await sourceRepository
                .GetResourceAsync<PackageMetadataResource>(session.CancellationToken)
                ;

            var metadata = await packageMetadataResource
                .GetMetadataAsync(packageIdentity, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken)
                ;

            if (metadata is null)
                return null;

            var versions = package.Versions;

            return new PackageInfo(packageIdentity, package, metadata.Vulnerabilities?.ToArray())
            {
                IsDeprecated = await metadata.GetDeprecationMetadataAsync() != null,
                IsOutdated = IsOutdated(packageIdentity, versions)
            };
        }
    }
}