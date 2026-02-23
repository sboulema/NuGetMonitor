using Microsoft.Extensions.Caching.Memory;
using NuGet.Common;
using NuGet.Frameworks;
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

        var package = await PackageCacheEntry.Get(packageId, session);

        session.ThrowIfCancellationRequested();

        return package;
    }

    public static async Task<PackageInfo?> GetPackageInfo(PackageIdentity? packageIdentity)
    {
        if (packageIdentity is null)
            return null;

        var session = _session;

        var packageInfo = await PackageInfoCacheEntry.Get(packageIdentity, session);

        session.ThrowIfCancellationRequested();

        return packageInfo;
    }

    public static async Task<PackageDetails?> GetPackageDetails(PackageInfo packageInfo)
    {
        return await PackageDetailsCacheEntry.Get(packageInfo.PackageIdentity, packageInfo.Package.RepositoryContext.SourceRepository, packageInfo.Session);
    }

    public static async Task<PackageDetails?> GetPackageDetails(PackageIdentity packageIdentity)
    {
        var packageInfo = await GetPackageInfo(packageIdentity);

        if (packageInfo is null)
            return null;

        return await GetPackageDetails(packageInfo);
    }

    public static async Task<ICollection<TransitiveDependencies>> GetTransitiveDependencies(ICollection<PackageReferenceInfo> allTopLevelPackages)
    {
        var topLevelPackagesByProject = allTopLevelPackages
            .SelectMany(item => item.PackageReferenceEntries.Where(entry => !entry.IsPrivateAsset).Select(entry => new { entry.ProjectItemInTargetFramework.Project, item.PackageInfo }))
            .GroupBy(item => item.Project)
            .ToDictionary(item => item.Key, item => item.Select(entry => entry.PackageInfo).ToArray());

        var transitiveDependenciesByProject = new Dictionary<ProjectInTargetFramework, TransitiveDependencies>();

        foreach (var project in topLevelPackagesByProject.Keys)
        {
            await EvaluateTransitiveDependencies(project, topLevelPackagesByProject, transitiveDependenciesByProject);
        }

        return transitiveDependenciesByProject.Values;
    }

    private static async Task EvaluateTransitiveDependencies(ProjectInTargetFramework projectInTargetFramework, Dictionary<ProjectInTargetFramework, PackageInfo[]> topLevelPackagesByProject, IDictionary<ProjectInTargetFramework, TransitiveDependencies> results)
    {
        var allProjects = topLevelPackagesByProject.Keys;

        var referencedProjects = projectInTargetFramework
            .GetReferencedProjects(allProjects)
            .ToArray();

        foreach (var project in referencedProjects)
        {
            await EvaluateTransitiveDependencies(project, topLevelPackagesByProject, results);
        }

        if (results.ContainsKey(projectInTargetFramework))
            return;

        var inheritedDependencies = referencedProjects
            .Select(results.GetValueOrDefault)
            .ExceptNullItems()
            .SelectMany(item => item.AllDependencies)
            .GroupBy(item => item.PackageIdentity.Id)
            .ToDictionary(item => item.Key, item => item.OrderByDescending(i => i.PackageIdentity.Version).First());

        var topLevelPackages = topLevelPackagesByProject[projectInTargetFramework];

        var transitiveDependencies = await GetTransitiveDependencies(projectInTargetFramework, topLevelPackages, inheritedDependencies);

        results[projectInTargetFramework] = transitiveDependencies;
    }

    /// <summary>
    /// Resolves all transitive (indirect) package dependencies for a project by walking the dependency tree.
    /// Applies central version management and vulnerability mitigations when applicable.
    /// Excludes dependencies introduced by referenced projects, only returns dependencies introduced by the project itself.
    /// </summary>
    public static async Task<TransitiveDependencies> GetTransitiveDependencies(ProjectInTargetFramework project, ICollection<PackageInfo> topLevelPackages, IReadOnlyDictionary<string, PackageInfo> inheritedDependencies)
    {
        var targetFramework = project.TargetFramework;

        // Initialize data structures for breadth-first traversal of the dependency graph
        var inputQueue = new Queue<PackageInfo>(topLevelPackages);
        // Track parent-child relationships to understand the dependency chain
        var parentsByChild = new Dictionary<PackageInfo, HashSet<PackageInfo>>();
        // Track processed packages to avoid duplicate processing and resolve version conflicts
        var processedItemsByPackageId = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);

        // Perform breadth-first traversal of the package dependency tree
        while (inputQueue.Count > 0)
        {
            var parentPackageReferenceInfo = inputQueue.Dequeue();

            var session = parentPackageReferenceInfo.Session;

            session.ThrowIfCancellationRequested();

            var parentPackageIdentity = parentPackageReferenceInfo.PackageIdentity;

            // Skip if this package is already included via an inherited dependency at a higher or equal version
            if (inheritedDependencies.TryGetValue(parentPackageIdentity.Id, out var inherited) && inherited.PackageIdentity.Version >= parentPackageIdentity.Version)
                continue;

            // Skip if we've already processed this package at a higher or equal version
            // This implements version conflict resolution - highest version wins
            if (processedItemsByPackageId.TryGetValue(parentPackageIdentity.Id, out var existing) && existing.PackageIdentity.Version >= parentPackageIdentity.Version)
                continue;

            processedItemsByPackageId[parentPackageIdentity.Id] = parentPackageReferenceInfo;

            // Get all direct dependencies of the current package for the target framework
            var dependencies = await parentPackageReferenceInfo.GetPackageDependenciesInFramework(targetFramework);

            foreach (var dependency in dependencies)
            {
                var dependentPackage = dependency;
                var dependentPackageIdentity = dependentPackage.PackageIdentity;

                session.ThrowIfCancellationRequested();

                // Apply transitive pinning if enabled via Central Package Management
                // This overrides transitive dependency versions with centrally managed versions
                if (project.IsTransitivePinningEnabled && project.CentralVersionMap.TryGetValue(dependentPackageIdentity.Id, out var versionSource))
                {
                    var version = versionSource.GetVersion();
                    if (version is not null && NuGetVersion.TryParse(version.OriginalString, out var parsedVersion) && parsedVersion > dependentPackageIdentity.Version)
                    {
                        var pinned = await PackageInfoCacheEntry.Get(new(dependentPackageIdentity.Id, parsedVersion), session);
                        if (pinned is not null)
                        {
                            dependentPackage = pinned;
                            dependentPackage.IsTransitivePinned = true;
                        }
                    }
                }

                // Apply any vulnerability mitigations defined for this package
                dependentPackage.VulnerabilityMitigation = project.PackageMitigations.GetValueOrDefault(dependentPackageIdentity);

                // Record the parent-child relationship for dependency tracking
                parentsByChild
                    .ForceValue(dependentPackage, _ => [])
                    .Add(parentPackageReferenceInfo);

                // Add the dependency to the queue for further processing
                inputQueue.Enqueue(dependentPackage);
            }
        }

        // Extract only the transitive dependencies (exclude top-level packages)
        var transitivePackageIdentities = processedItemsByPackageId.Values
            .Where(item => !topLevelPackages.Contains(item))
            .Select(item => item.PackageIdentity)
            .ToHashSet();

        // Filter parent-child relationships to include only transitive packages
        parentsByChild = parentsByChild
            .Where(item => transitivePackageIdentities.Contains(item.Key.PackageIdentity))
            .ToDictionary();

        return new(project, topLevelPackages, inheritedDependencies.Values.ToArray(), new TransitivePackages(parentsByChild));
    }

    private static async Task<PackageReferenceInfo?> FindPackageInfo(PackageReference item, HashSet<PackageReferenceEntry> packageReferenceEntries, NuGetSession session)
    {
        var package = await PackageCacheEntry.Get(item.Id, session);

        var identity = item.FindBestMatch(package?.Versions);
        if (identity is null)
            return null;

        var packageInfo = await PackageInfoCacheEntry.Get(identity, session);
        return packageInfo is null ? null : new PackageReferenceInfo(packageInfo, packageReferenceEntries);
    }

    private static async Task<PackageInfo[]> GetPackageDependenciesInFramework(this PackageInfo packageInfo, NuGetFramework targetFramework)
    {
        return await PackageDependenciesInFrameworkCacheEntry.Get(packageInfo, targetFramework) ?? Array.Empty<PackageInfo>();
    }

    private static bool IsOutdated(PackageIdentity packageIdentity, IEnumerable<NuGetVersion> versions)
    {
        var latestVersion = versions.FirstOrDefault(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease) ?? new NuGetVersion(0, 0, 0);

        return latestVersion > packageIdentity.Version;
    }

    private static NuGetFramework ToPlatformVersionIndependent(NuGetFramework framework)
    {
        // e.g net6.0-windows project can use net6.0-windows7.0 package
        return new(framework.Framework, framework.Version, framework.Platform, FrameworkConstants.EmptyVersion);
    }

    public static T? GetNearestFramework<T>(this IReadOnlyCollection<T> items, NuGetFramework framework)
        where T : class, IFrameworkSpecific
    {
        return NuGetFrameworkUtility.GetNearest(items, framework)
               ?? NuGetFrameworkUtility.GetNearest(items, ToPlatformVersionIndependent(framework), item => ToPlatformVersionIndependent(item.TargetFramework));
    }

    public static bool IsCompatibleWith(this NuGetFramework projectFramework, NuGetFramework candidate)
    {
        return NuGetFrameworkUtility.IsCompatibleWithFallbackCheck(ToPlatformVersionIndependent(projectFramework), ToPlatformVersionIndependent(candidate));
    }

    private abstract class CacheEntry<T> where T : class
    {
        private readonly TaskCompletionSource<T?> _taskCompletionSource = new();

        protected CacheEntry(Func<Task<T?>> generator)
        {
            Load(generator);
        }

        protected Task<T?> GetValue()
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

        protected static TEntry GetOrCreate<TEntry>(NuGetSession session, object key, Func<TEntry> factory)
        {
            lock (session)
            {
                return session.Cache.GetOrCreate(key, _ => factory()) ?? throw new InvalidOperationException("Failed to get item from cache");
            }
        }
    }

    private sealed class PackageCacheEntry : CacheEntry<Package>
    {
        private PackageCacheEntry(string packageId, NuGetSession session)
            : base(() => GetPackage(packageId, session))
        {
        }

        public static Task<Package?> Get(string packageId, NuGetSession session)
        {
            return GetOrCreate(session, packageId, () => new PackageCacheEntry(packageId, session)).GetValue();
        }

        private static async Task<Package?> GetPackage(string packageId, NuGetSession session)
        {
            var sourceRepositories = session.SourceRepositories;
            if (sourceRepositories.Count == 0)
                return null;

            foreach (var repositoryContext in sourceRepositories)
            {
                var unsortedVersions = await GetPackageVersions(packageId, session, repositoryContext);

                if (unsortedVersions.Length <= 0)
                    continue;

                var versions = unsortedVersions.OrderByDescending(item => item).ToArray();
                return new(packageId, versions, repositoryContext);
            }

            // No versions found in any source, this package is probably unlisted
            return new(packageId, [], sourceRepositories.First());
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
        private PackageInfoCacheEntry(PackageIdentity packageIdentity, NuGetSession session)
            : base(() => GetPackageInfo(packageIdentity, session))
        {
        }

        public static Task<PackageInfo?> Get(PackageIdentity packageIdentity, NuGetSession session)
        {
            return GetOrCreate(session, packageIdentity, () => new PackageInfoCacheEntry(packageIdentity, session)).GetValue();
        }

        private static async Task<PackageInfo?> GetPackageInfo(PackageIdentity packageIdentity, NuGetSession session)
        {
            var package = await PackageCacheEntry.Get(packageIdentity.Id, session);
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

    private sealed class PackageDetailsCacheEntry : CacheEntry<PackageDetails>
    {
        private PackageDetailsCacheEntry(PackageIdentity packageIdentity, SourceRepository repository, NuGetSession session)
            : base(() => GetDetails(packageIdentity, repository, session))
        {
        }

        public static Task<PackageDetails?> Get(PackageIdentity packageIdentity, SourceRepository repository, NuGetSession session)
        {
            return GetOrCreate(session, new CacheEntryKey(packageIdentity), () => new PackageDetailsCacheEntry(packageIdentity, repository, session)).GetValue();
        }

        private static async Task<PackageDetails?> GetDetails(PackageIdentity packageIdentity, SourceRepository repository, NuGetSession session)
        {
            // Don't scan packages with pseudo-references, they don't get physically included, but cause vulnerability warnings.
            if (string.Equals(packageIdentity.Id, NetStandardPackageId, StringComparison.OrdinalIgnoreCase))
                return new(packageIdentity, [], []);

            var resource = await repository.GetResourceAsync<DownloadResource>(session.CancellationToken);

            using var downloadResult = await resource.GetDownloadResourceResultAsync(packageIdentity, session.PackageDownloadContext, session.GlobalPackagesFolder, NullLogger.Instance, session.CancellationToken);

            if (downloadResult.Status != DownloadResourceResultStatus.Available)
                return new(packageIdentity, [], []);

            var dependencyGroups = await downloadResult.PackageReader.GetPackageDependenciesAsync(session.CancellationToken);

            var supportedFrameworks = downloadResult.PackageReader.GetSupportedFrameworks();

            return new(packageIdentity, dependencyGroups.ToArray(), supportedFrameworks.ToArray());
        }

        // ReSharper disable once NotAccessedPositionalProperty.Local
        private sealed record CacheEntryKey(PackageIdentity PackageIdentity);
    }

    private sealed class PackageDependenciesInFrameworkCacheEntry : CacheEntry<PackageInfo[]>
    {
        private PackageDependenciesInFrameworkCacheEntry(PackageInfo packageInfo, NuGetFramework targetFramework)
            : base(() => GetDirectDependencies(packageInfo, targetFramework))
        {
        }

        public static Task<PackageInfo[]?> Get(PackageInfo packageInfo, NuGetFramework targetFramework)
        {
            return GetOrCreate(packageInfo.Session, new CacheEntryKey(packageInfo.PackageIdentity, targetFramework), () => new PackageDependenciesInFrameworkCacheEntry(packageInfo, targetFramework)).GetValue();
        }

        private static async Task<PackageInfo[]?> GetDirectDependencies(PackageInfo packageInfo, NuGetFramework targetFramework)
        {
            var session = packageInfo.Session;
            var sourceRepository = packageInfo.Package.RepositoryContext.SourceRepository;

            var packageDetails = await PackageDetailsCacheEntry.Get(packageInfo.PackageIdentity, sourceRepository, session);

            var dependencyGroups = packageDetails?.DependencyGroups;

            if (dependencyGroups is not { Count: > 0 })
                return [];

            var dependentPackages = dependencyGroups.GetNearestFramework(targetFramework)?.Packages
                                    // fallback to all when GetNearestFramework fails, better have some false positives than to miss one
                                    ?? dependencyGroups.SelectMany(group => group.Packages).Distinct();

            async Task<PackageInfo?> ToPackageInfo(PackageDependency packageDependency)
            {
                var package = await PackageCacheEntry.Get(packageDependency.Id, session);

                if (package is null)
                    return null;

                var dependencyVersion = packageDependency.VersionRange.FindBestMatch(package.Versions);

                var info = await PackageInfoCacheEntry.Get(new(package.Id, dependencyVersion), session);

                return info;
            }

            var packageInfos = await Task.WhenAll(dependentPackages.Select(ToPackageInfo));

            return packageInfos?.ExceptNullItems().ToArray();
        }

        // ReSharper disable NotAccessedPositionalProperty.Local
        private sealed record CacheEntryKey(PackageIdentity PackageIdentity, NuGetFramework TargetFramework);
        // ReSharper restore NotAccessedPositionalProperty.Local
    }
}
