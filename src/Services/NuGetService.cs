﻿using System.IO;
using System.Security.Principal;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;
using PackageReference = NuGetMonitor.Models.PackageReference;

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

    public static async Task<ICollection<PackageReferenceInfo>> CheckPackageReferences(IEnumerable<PackageReferenceEntry> packageReferences)
    {
        var session = _session;

        session.ThrowIfCancellationRequested();

        var getPackageInfoTasks = packageReferences
            .GroupBy(item => item.Identity)
            .Select(group => FindPackageInfo(group.Key, group.ToHashSet(), session));

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

    public static async Task<ICollection<TransitiveDependencies>> GetTransitivePackages(IEnumerable<PackageReferenceEntry> packageReferences, ICollection<PackageReferenceInfo> topLevelPackages)
    {
        var results = new List<TransitiveDependencies>();

        var packagesReferencesByProject = packageReferences.GroupBy(item => item.ProjectItem.Project);

        foreach (var projectPackageReferences in packagesReferencesByProject)
        {
            var project = projectPackageReferences.Key;

            var targetFrameworks = project.GetTargetFrameworks();
            if (targetFrameworks is null)
            {
                await LoggingService.LogAsync($"No target framework found in project {Path.GetFileName(project.FullPath)} (old project format?) - skipping transitive package analysis.");
                continue;
            }

            var topLevelPackagesInProject = topLevelPackages
                .Where(package => projectPackageReferences.Any(item => package.PackageReferenceEntries.Contains(item)))
                .Select(item => item.PackageInfo)
                .ToArray();

            foreach (var targetFramework in targetFrameworks)
            {
                var inputQueue = new Queue<PackageInfo>(topLevelPackagesInProject);
                var parentsByChild = new Dictionary<PackageInfo, HashSet<PackageInfo>>();
                var processedItemsByPackageId = new Dictionary<string, PackageInfo>();

                while (inputQueue.Count > 0)
                {
                    var packageInfo = inputQueue.Dequeue();

                    var packageIdentity = packageInfo.PackageIdentity;

                    if (processedItemsByPackageId.TryGetValue(packageIdentity.Id, out var existing) && existing.PackageIdentity.Version >= packageIdentity.Version)
                        continue;

                    processedItemsByPackageId[packageIdentity.Id] = packageInfo;

                    var dependencies = await packageInfo.GetPackageDependenciesInFramework(targetFramework);

                    foreach (var dependency in dependencies)
                    {
                        parentsByChild
                            .ForceValue(dependency, _ => new HashSet<PackageInfo>())
                            .Add(packageInfo);

                        inputQueue.Enqueue(dependency);
                    }
                }

                var transitivePackages = processedItemsByPackageId.Values.Except(topLevelPackages.Select(item => item.PackageInfo)).ToHashSet();

                parentsByChild = parentsByChild
                    .Where(item => transitivePackages.Contains(item.Key))
                    .ToDictionary();

                results.Add(new TransitiveDependencies(project, targetFramework, parentsByChild));
            }
        }

        return results;
    }

    private static async Task<PackageReferenceInfo?> FindPackageInfo(PackageReference item, HashSet<PackageReferenceEntry> packageReferenceEntries, NuGetSession session)
    {
        var package = await GetPackageCacheEntry(item.Id, session).GetValue();

        var identity = item.FindBestMatch(package?.Versions);
        if (identity is null)
            return null;

        var packageInfo = await GetPackageInfoCacheEntry(identity, session).GetValue();
        return packageInfo is null ? null : new PackageReferenceInfo(packageInfo, packageReferenceEntries);
    }

    private static async Task<PackageInfo[]> GetPackageDependenciesInFramework(this PackageInfo packageInfo, NuGetFramework targetFramework)
    {
        return await GetPackageDependenciesInFrameworkCacheEntry(packageInfo, targetFramework).GetValue() ?? Array.Empty<PackageInfo>();
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

    // ReSharper disable NotAccessedPositionalProperty.Local
    private sealed record PackageDependencyCacheEntryKey(PackageIdentity PackageIdentity);

    private sealed record PackageDependenciesInFrameworkCacheEntryKey(PackageIdentity PackageIdentity, NuGetFramework TargetFramework);
    // ReSharper restore NotAccessedPositionalProperty.Local

    private static PackageDependenciesCacheEntry GetPackageDependencyCacheEntry(PackageIdentity packageIdentity, SourceRepository repository, NuGetSession session)
    {
        PackageDependenciesCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);

            return new PackageDependenciesCacheEntry(packageIdentity, repository, session);
        }

        lock (session)
        {
            return session.Cache.GetOrCreate(new PackageDependencyCacheEntryKey(packageIdentity), Factory) ??
                   throw new InvalidOperationException("Failed to get package dependency from cache");
        }
    }

    private static PackageDependenciesInFrameworkCacheEntry GetPackageDependenciesInFrameworkCacheEntry(PackageInfo packageInfo, NuGetFramework targetFramework)
    {
        PackageDependenciesInFrameworkCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);

            return new PackageDependenciesInFrameworkCacheEntry(packageInfo, targetFramework);
        }

        _session.ThrowIfCancellationRequested();

        lock (_session)
        {
            return _session.Cache.GetOrCreate(new PackageDependenciesInFrameworkCacheEntryKey(packageInfo.PackageIdentity, targetFramework), Factory) ??
                   throw new InvalidOperationException("Failed to get package dependency in framework from cache");
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
                var packageResource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>(session.CancellationToken);

                var unsortedVersions = await packageResource.GetAllVersionsAsync(packageId, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken);

                var versions = unsortedVersions?.OrderByDescending(item => item).ToArray();

                if (versions?.Length > 0)
                {
                    return new Package(packageId, versions, sourceRepository);
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

            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(session.CancellationToken);

            var metadata = await packageMetadataResource.GetMetadataAsync(packageIdentity, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken);

            if (metadata is null)
                return null;

            var versions = package.Versions;

            var deprecationMetadata = await metadata.GetDeprecationMetadataAsync();

            return new PackageInfo(packageIdentity, package, session, metadata.Vulnerabilities?.ToArray(), deprecationMetadata)
            {
                IsOutdated = IsOutdated(packageIdentity, versions)
            };
        }
    }

    private sealed class PackageDependenciesCacheEntry : CacheEntry<PackageDependencyGroup[]>
    {
        public PackageDependenciesCacheEntry(PackageIdentity packageIdentity, SourceRepository repository, NuGetSession session)
            : base(() => GetDirectDependencies(packageIdentity, repository, session))
        {
        }

        private static async Task<PackageDependencyGroup[]?> GetDirectDependencies(PackageIdentity packageIdentity, SourceRepository repository, NuGetSession session)
        {
            // Don't scan packages with pseudo-references, they don't get physically included, but cause vulnerability warnings.
            if (string.Equals(packageIdentity.Id, "NETStandard.Library", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<PackageDependencyGroup>();

            var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

            var packageStream = new MemoryStream();
            await resource.CopyNupkgToStreamAsync(packageIdentity.Id, packageIdentity.Version, packageStream, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken);

            if (packageStream.Length == 0)
                return Array.Empty<PackageDependencyGroup>();

            packageStream.Position = 0;

            using var package = new PackageArchiveReader(packageStream);

            var dependencyGroups = package.GetPackageDependencies().ToArray();

            return dependencyGroups;
        }
    }

    private sealed class PackageDependenciesInFrameworkCacheEntry : CacheEntry<PackageInfo[]>
    {
        public PackageDependenciesInFrameworkCacheEntry(PackageInfo packageInfo, NuGetFramework targetFramework)
            : base(() => GetDirectDependencies(packageInfo, targetFramework))
        {
        }

        private static async Task<PackageInfo[]?> GetDirectDependencies(PackageInfo packageInfo, NuGetFramework targetFramework)
        {
            var session = packageInfo.Session;

            var dependencyGroups = await GetPackageDependencyCacheEntry(packageInfo.PackageIdentity, packageInfo.Package.SourceRepository, session).GetValue();

            if (dependencyGroups is null)
                return Array.Empty<PackageInfo>();

            var dependencyGroup = NuGetFrameworkUtility.GetNearest(dependencyGroups, targetFramework, item => item.TargetFramework);
            if (dependencyGroup is null)
                return Array.Empty<PackageInfo>();

            async Task<PackageInfo?> ToPackageInfo(PackageDependency packageDependency)
            {
                var package = await GetPackageCacheEntry(packageDependency.Id, session).GetValue();

                if (package is null)
                    return null;

                var dependencyVersion = packageDependency.VersionRange.FindBestMatch(package.Versions);

                var info = await GetPackageInfoCacheEntry(new PackageIdentity(package.Id, dependencyVersion), session).GetValue();

                return info;
            }

            var packageInfos = await Task.WhenAll(dependencyGroup.Packages.Select(ToPackageInfo));

            return packageInfos?.ExceptNullItems().ToArray();
        }
    }
}