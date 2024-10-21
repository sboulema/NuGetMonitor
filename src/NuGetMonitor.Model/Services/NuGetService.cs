using Microsoft.Extensions.Caching.Memory;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGetMonitor.Model.Models;
using TomsToolbox.Essentials;
using PackageReference = NuGetMonitor.Model.Models.PackageReference;

namespace NuGetMonitor.Model.Services;

public static class NuGetService
{
    private static bool _shutdownInitiated;

    private static NuGetSession _session = new(null);

    public static void Shutdown()
    {
        _shutdownInitiated = true;
        _session.Dispose();
    }

    public static void Reset(string? solutionFolder)
    {
        if (_shutdownInitiated)
            return;

        Interlocked.Exchange(ref _session, new(solutionFolder)).Dispose();
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

    public static async Task<ICollection<TransitiveDependencies>> GetTransitivePackages(ICollection<PackageReferenceInfo> allTopLevelPackages)
    {
        var topLevelPackagesByProject = allTopLevelPackages
            .SelectMany(item => item.PackageReferenceEntries.Select(entry => new { entry.ProjectItemInTargetFramework.Project, item.PackageInfo }))
            .GroupBy(item => item.Project)
            .ToDictionary(item => item.Key, item => item.Select(entry => entry.PackageInfo).ToArray());

        return await Task.WhenAll(topLevelPackagesByProject.Select(item => GetTransitiveDependencies(item.Key, item.Value)));
    }

    public static async Task<TransitiveDependencies> GetTransitiveDependencies(ProjectInTargetFramework project, ICollection<PackageInfo> topLevelPackages)
    {
        var targetFramework = project.TargetFramework;

        var inputQueue = new Queue<PackageInfo>(topLevelPackages);
        var parentsByChild = new Dictionary<PackageInfo, HashSet<PackageInfo>>();
        var processedItemsByPackageId = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);

        while (inputQueue.Count > 0)
        {
            var packageReferenceInfo = inputQueue.Dequeue();

            var session = packageReferenceInfo.Session;

            session.ThrowIfCancellationRequested();

            var packageIdentity = packageReferenceInfo.PackageIdentity;

            if (processedItemsByPackageId.TryGetValue(packageIdentity.Id, out var existing) && existing.PackageIdentity.Version >= packageIdentity.Version)
                continue;

            processedItemsByPackageId[packageIdentity.Id] = packageReferenceInfo;

            var dependencies = await packageReferenceInfo.GetPackageDependenciesInFramework(targetFramework);

            foreach (var dependency in dependencies)
            {
                var package = dependency;
                var identity = package.PackageIdentity;

                session.ThrowIfCancellationRequested();

                if (project.IsTransitivePinningEnabled && project.CentralVersionMap.TryGetValue(identity.Id, out var versionSource))
                {
                    var version = versionSource.GetVersion();
                    if (version is not null && NuGetVersion.TryParse(version.OriginalString, out var parsedVersion) && parsedVersion > identity.Version)
                    {
                        var pinned = await GetPackageInfoCacheEntry(new(identity.Id, parsedVersion), session).GetValue();
                        if (pinned is not null)
                        {
                            package = pinned;
                            package.IsPinned = true;
                        }
                    }
                }

                package.VulnerabilityMitigation = project.PackageMitigations.GetValueOrDefault(identity);

                parentsByChild
                    .ForceValue(package, _ => new())
                    .Add(packageReferenceInfo);

                inputQueue.Enqueue(package);
            }
        }

        var transitivePackageIdentities = processedItemsByPackageId.Values
            .Where(item => !topLevelPackages.Contains(item))
            .Select(item => item.PackageIdentity)
            .ToHashSet();

        parentsByChild = parentsByChild
            .Where(item => transitivePackageIdentities.Contains(item.Key.PackageIdentity))
            .ToDictionary();

        return new(project, parentsByChild);
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

            return new(packageId, session);
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

            return new(packageIdentity, session);
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

            return new(packageIdentity, repository, session);
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

            return new(packageInfo, targetFramework);
        }

        var session = packageInfo.Session;

        lock (session)
        {
            return session.Cache.GetOrCreate(new PackageDependenciesInFrameworkCacheEntryKey(packageInfo.PackageIdentity, targetFramework), Factory)
                   ?? throw new InvalidOperationException("Failed to get package dependency in framework from cache");
        }
    }

    private static bool IsOutdated(PackageIdentity packageIdentity, IEnumerable<NuGetVersion> versions)
    {
        var latestVersion = versions.FirstOrDefault(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease) ?? new NuGetVersion(0, 0, 0);

        return latestVersion > packageIdentity.Version;
    }

    private static NuGetFramework ToPlatformVersionIndependent(NuGetFramework framework)
    {
        return new(framework.Framework, framework.Version, framework.Platform, FrameworkConstants.EmptyVersion);
    }

    private static T? GetNearestFramework<T>(ICollection<T> items, NuGetFramework framework)
        where T : class, IFrameworkSpecific
    {
        return NuGetFrameworkUtility.GetNearest(items, framework)
               // e.g net6.0-windows project can use net6.0-windows7.0 package
               ?? NuGetFrameworkUtility.GetNearest(items, ToPlatformVersionIndependent(framework), item => ToPlatformVersionIndependent(item.TargetFramework));
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
            foreach (var repositoryContext in session.SourceRepositories)
            {
                var unsortedVersions = await GetPackageVersions(packageId, session, repositoryContext);

                if (unsortedVersions.Length <= 0)
                    continue;

                var versions = unsortedVersions.OrderByDescending(item => item).ToArray();
                return new(packageId, versions, repositoryContext);
            }

            return null;
        }

        private static async Task<NuGetVersion[]> GetPackageVersions(string packageId, NuGetSession session, RepositoryContext repositoryContext)
        {
            if (repositoryContext.IsDependencyInfoSupported)
            {
                try
                {
                    var dependencyInfoResource = await repositoryContext.SourceRepository.GetResourceAsync<DependencyInfoResource>(session.CancellationToken);

                    var packages = await dependencyInfoResource.ResolvePackages(packageId, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken);

                    return packages
                        .Where(item => item.Listed)
                        .Select(item => item.Identity.Version)
                        .ToArray();
                }
                catch (NotSupportedException)
                {
                    repositoryContext.IsDependencyInfoSupported = false;
                }
            }

            if (repositoryContext.IsAccessible)
            {
                try
                {
                    var packageResource = await repositoryContext.SourceRepository.GetResourceAsync<FindPackageByIdResource>(session.CancellationToken);

                    var versions = await packageResource.GetAllVersionsAsync(packageId, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken);

                    return versions.ToArray();
                }
                catch (Exception ex)
                {
                    repositoryContext.AccessError(ex);
                }
            }

            return Array.Empty<NuGetVersion>();
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

            var sourceRepository = package.RepositoryContext.SourceRepository;

            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(session.CancellationToken);

            var metadata = await packageMetadataResource.GetMetadataAsync(packageIdentity, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken);

            if (metadata is null)
                return null;

            var versions = package.Versions;

            var deprecationMetadata = await metadata.GetDeprecationMetadataAsync();

            return new(packageIdentity, package, session, metadata.Vulnerabilities?.ToArray(), deprecationMetadata, metadata.ProjectUrl)
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
            if (string.Equals(packageIdentity.Id, NetStandardPackageId, StringComparison.OrdinalIgnoreCase))
                return Array.Empty<PackageDependencyGroup>();

            var resource = await repository.GetResourceAsync<DownloadResource>(session.CancellationToken);

            using var downloadResult = await resource.GetDownloadResourceResultAsync(packageIdentity, session.PackageDownloadContext, session.GlobalPackagesFolder, NullLogger.Instance, session.CancellationToken);

            if (downloadResult.Status != DownloadResourceResultStatus.Available)
                return Array.Empty<PackageDependencyGroup>();

            var dependencyGroups = await downloadResult.PackageReader.GetPackageDependenciesAsync(session.CancellationToken);

            return dependencyGroups.ToArray();
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
            var sourceRepository = packageInfo.Package.RepositoryContext.SourceRepository;

            var dependencyGroups = await GetPackageDependencyCacheEntry(packageInfo.PackageIdentity, sourceRepository, session).GetValue();

            if (dependencyGroups == null || dependencyGroups.Length < 1)
                return Array.Empty<PackageInfo>();

            var dependentPackages = GetNearestFramework(dependencyGroups, targetFramework)?.Packages
                                    // fallback to all when GetNearestFramework fails, better have some false positives than to miss one
                                    ?? dependencyGroups.SelectMany(group => group.Packages).Distinct();

            async Task<PackageInfo?> ToPackageInfo(PackageDependency packageDependency)
            {
                var package = await GetPackageCacheEntry(packageDependency.Id, session).GetValue();

                if (package is null)
                    return null;

                var dependencyVersion = packageDependency.VersionRange.FindBestMatch(package.Versions);

                var info = await GetPackageInfoCacheEntry(new(package.Id, dependencyVersion), session).GetValue();

                return info;
            }

            var packageInfos = await Task.WhenAll(dependentPackages.Select(ToPackageInfo));

            return packageInfos?.ExceptNullItems().ToArray();
        }
    }
}
