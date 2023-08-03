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
    private static Session _session = new();

    public static void ClearCache()
    {
        Interlocked.Exchange(ref _session, new Session()).Dispose();
    }

    public static async Task<IEnumerable<PackageInfo>> CheckPackageReferences(IEnumerable<PackageReferenceEntry> references)
    {
        var session = _session;

        var identitiesById = references
            .Select(item => item.Identity)
            .GroupBy(item => item.Id);

        var getPackageInfoTasks = identitiesById.Select(item => GetPackageInfo(item, session));

        var result = await Task.WhenAll(getPackageInfoTasks).ConfigureAwait(false);

        return result.ExceptNullItems();
    }

    public static async Task<Package> GetPackage(string packageId)
    {
        return await GetPackageCacheEntry(packageId, _session).GetPackage();
    }

    private static async Task<PackageInfo?> GetPackageInfo(IGrouping<string, PackageIdentity> packageIdentities, Session session)
    {
        var (_, versions, sourceRepository) = await GetPackageCacheEntry(packageIdentities.Key, session).GetPackage().ConfigureAwait(false);

        if (sourceRepository is null)
        {
            return null;
        }

        // use the oldest reference with the smallest version
        var packageIdentity = packageIdentities.OrderBy(item => item.Version.Version).First();

        var packageMetadataResource = await sourceRepository
            .GetResourceAsync<PackageMetadataResource>(session.CancellationToken)
            .ConfigureAwait(false);

        var metadata = await packageMetadataResource
            .GetMetadataAsync(packageIdentity, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken)
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

    private static PackageCacheEntry GetPackageCacheEntry(string packageId, Session session)
    {
        PackageCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            return new PackageCacheEntry(packageId, session);
        }

        return session.PackageCache.GetOrCreate(packageId, Factory) ?? throw new InvalidOperationException("Failed to get package from cache");
    }

    private static bool IsOutdated(PackageIdentity packageIdentity, IEnumerable<NuGetVersion> versions)
    {
        var latestVersion = versions.FirstOrDefault(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease);

        return latestVersion > packageIdentity.Version;
    }

    private static async Task<(SourceRepository?, ICollection<NuGetVersion>)> GetPackageVersions(string packageId, Session session)
    {
        foreach (var sourceRepository in await session.GetSourceRepositories().ConfigureAwait(false))
        {
            var packageResource = await sourceRepository
                .GetResourceAsync<FindPackageByIdResource>(session.CancellationToken)
                .ConfigureAwait(false);

            var unsortedVersions = await packageResource
                .GetAllVersionsAsync(packageId, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken)
                .ConfigureAwait(false);

            var versions = unsortedVersions?.OrderByDescending(item => item).ToArray();

            if (versions?.Length > 0)
            {
                return (sourceRepository, versions);
            }
        }

        return (null, Array.Empty<NuGetVersion>());
    }

    private sealed class Semaphore<T> : IDisposable
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

    private class PackageCacheEntry
    {
        private readonly TaskCompletionSource<Package> _taskCompletionSource = new();

        public PackageCacheEntry(string packageId, Session session)
        {
            Load(packageId, session);
        }

        public Task<Package> GetPackage()
        {
            return _taskCompletionSource.Task;
        }

        private async void Load(string packageId, Session session)
        {
            try
            {
                // var (sourceRepository, versions) = await Task.Run(async () => await GetPackageVersions(packageId, session).ConfigureAwait(false)).ConfigureAwait(false);
                var (sourceRepository, versions) = await GetPackageVersions(packageId, session).ConfigureAwait(false);

                var package = new Package(packageId, versions, sourceRepository);

                _taskCompletionSource.SetResult(package);
            }
            catch
            {
                _taskCompletionSource.SetResult(new Package(packageId, Array.Empty<NuGetVersion>(), null));
            }
        }
    }

    private class Session : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private ICollection<SourceRepository>? _sourceRepositories;

        public readonly MemoryCache PackageCache = new(new MemoryCacheOptions { });

        public readonly SourceCacheContext SourceCacheContext = new();

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public async Task<ICollection<SourceRepository>> GetSourceRepositories()
        {
            static async Task<ICollection<SourceRepository>> Get()
            {
                var solution = await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(false);
                var solutionDirectory = Path.GetDirectoryName(solution?.FullPath);

                var packageSourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(solutionDirectory));
                var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
                var sourceRepositories = sourceRepositoryProvider.GetRepositories();

                return sourceRepositories.ToArray();
            }

            using (new Semaphore<Session>())
            {
                return _sourceRepositories ??= await Get().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }
}