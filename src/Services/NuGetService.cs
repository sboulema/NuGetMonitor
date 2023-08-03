using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;
using Settings = NuGet.Configuration.Settings;

namespace NuGetMonitor.Services;

public record Package(string Id, ICollection<NuGetVersion> Versions, SourceRepository? SourceRepository)
{
    public override string ToString()
    {
        return string.Join(", ", Versions);
    }
}

public static class NuGetService
{
    private static readonly MemoryCache _packageCache = new(new MemoryCacheOptions { });

    private static SourceCacheContext _sourceCacheContext = new();

    private static ICollection<SourceRepository>? _sourceRepositories;

    public static void ClearCache()
    {
        using (new Semaphore())
        {
            _sourceRepositories = null;

            _sourceCacheContext.Dispose();
            _sourceCacheContext = new SourceCacheContext();

            _packageCache.Clear();
        }
    }

    public static async Task<IEnumerable<PackageInfo>> CheckPackageReferences(IReadOnlyCollection<PackageReferenceEntry> references)
    {
        var identitiesById = references
            .Select(item => item.Identity)
            .GroupBy(item => item.Id);

        var getPackageInfoTasks = identitiesById.Select(GetPackageInfo);

        var result = await Task
            .WhenAll(getPackageInfoTasks)
            .ConfigureAwait(false);

        return result.ExceptNullItems();
    }

    private static async Task<PackageInfo?> GetPackageInfo(IGrouping<string, PackageIdentity> packageIdentities)
    {
        using (new Semaphore())
        {
            var (_, versions, sourceRepository) = await GetPackage(packageIdentities.Key).ConfigureAwait(false);

            // use the oldest reference with the smallest version
            var packageIdentity = packageIdentities.OrderBy(item => item.Version.Version).First();

            if (sourceRepository is null)
            {
                return null;
            }

            var packageMetadataResource = await sourceRepository
                .GetResourceAsync<PackageMetadataResource>()
                .ConfigureAwait(false);

            var metadata = await packageMetadataResource
                .GetMetadataAsync(packageIdentity, _sourceCacheContext, NullLogger.Instance, CancellationToken.None)
                .ConfigureAwait(false);

            if (metadata is null)
            {
                return null;
            }

            return new PackageInfo(packageIdentity)
            {
                IsVulnerable = metadata.Vulnerabilities != null,
                IsDeprecated = await metadata.GetDeprecationMetadataAsync().ConfigureAwait(false) != null,
                IsOutdated = IsOutdated(packageIdentity, versions)
            };
        }
    }

    public static async Task<Package> GetPackage(string packageId)
    {
        async Task<Package> Factory(ICacheEntry cacheEntry)
        {
            var (sourceRepository, versions) = await GetPackageVersions(packageId).ConfigureAwait(false);

            var package = new Package(packageId, versions, sourceRepository);

            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            return package;
        }

        using (new Semaphore())
        {
            return (await _packageCache.GetOrCreateAsync(packageId, Factory).ConfigureAwait(false)) ?? throw new InvalidOperationException("Failed to get package from cache");
        }
    }

    private static bool IsOutdated(PackageIdentity packageIdentity, IEnumerable<NuGetVersion> versions)
    {
        var latestVersion = versions.Last(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease);

        return latestVersion > packageIdentity.Version;
    }

    private static async Task<(SourceRepository?, ICollection<NuGetVersion>)> GetPackageVersions(string packageId)
    {
        using (new Semaphore())
        {
            foreach (var sourceRepository in await GetSourceRepositories().ConfigureAwait(false))
            {
                var packageResource = await sourceRepository
                    .GetResourceAsync<FindPackageByIdResource>()
                    .ConfigureAwait(false);

                var versions = (await packageResource
                        .GetAllVersionsAsync(packageId, _sourceCacheContext, NullLogger.Instance,
                            CancellationToken.None)
                        .ConfigureAwait(false))
                    ?.OrderByDescending(item => item).ToArray();

                if (versions?.Length > 0)
                {
                    return (sourceRepository, versions);
                }
            }

            return (null, Array.Empty<NuGetVersion>());
        }
    }

    private static async Task<ICollection<SourceRepository>> GetSourceRepositories()
    {
        using (new Semaphore())
        {
            static async Task<ICollection<SourceRepository>> Get()
            {
                var solution = await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(false);
                var solutionDirectory = Path.GetDirectoryName(solution?.FullPath);

                var packageSourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(solutionDirectory));
                var sourceRepositoryProvider =
                    new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
                var sourceRepositories = sourceRepositoryProvider.GetRepositories();

                return sourceRepositories.ToArray();
            }

            return _sourceRepositories ??= await Get().ConfigureAwait(false);
        }
    }

    private sealed class Semaphore : IDisposable
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        public Semaphore()
        {
            _ = _semaphore.WaitAsync();
        }

        public void Dispose()
        {
            _semaphore.Release();
        }
    }
}