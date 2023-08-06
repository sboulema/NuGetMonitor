using System;
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

    public static void Shutdown()
    {
        _session.Dispose();
    }

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

    public static async Task<Package?> GetPackage(string packageId)
    {
        return await GetPackageCacheEntry(packageId, _session).GetValue().ConfigureAwait(false);
    }

    public static async Task<PackageInfo?> GetPackageInfo(PackageIdentity packageIdentity)
    {
        return await GetPackageInfoCacheEntry(packageIdentity, _session).GetValue().ConfigureAwait(false);
    }

    private static async Task<PackageInfo?> GetPackageInfo(IEnumerable<PackageIdentity> packageIdentities, Session session)
    {
        // if multiple version are provided, use the oldest reference with the smallest version
        var packageIdentity = packageIdentities.OrderBy(item => item.Version.Version).First();

        return await GetPackageInfoCacheEntry(packageIdentity, session).GetValue().ConfigureAwait(false);
    }

    private static PackageCacheEntry GetPackageCacheEntry(string packageId, Session session)
    {
        PackageCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            return new PackageCacheEntry(packageId, session);
        }

        lock (session)
        {
            return session.Cache.GetOrCreate(packageId, Factory) ?? throw new InvalidOperationException("Failed to get package from cache");
        }
    }

    private static PackageInfoCacheEntry GetPackageInfoCacheEntry(PackageIdentity packageIdentity, Session session)
    {
        PackageInfoCacheEntry Factory(ICacheEntry cacheEntry)
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);

            return new PackageInfoCacheEntry(packageIdentity, session);
        }

        lock (session)
        {
            return session.Cache.GetOrCreate(packageIdentity, Factory) ?? throw new InvalidOperationException("Failed to get package from cache");
        }
    }

    private static bool IsOutdated(PackageIdentity packageIdentity, IEnumerable<NuGetVersion> versions)
    {
        var latestVersion = versions.FirstOrDefault(version => version.IsPrerelease == packageIdentity.Version.IsPrerelease);

        return latestVersion > packageIdentity.Version;
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

    private abstract class CacheEntry<T> where T: class
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
                var value = await generator().ConfigureAwait(false);

                _taskCompletionSource.SetResult(value);
            }
            catch
            {
                _taskCompletionSource.SetResult(default);
            }
        }
    }

    private class PackageCacheEntry : CacheEntry<Package>
    {
        public PackageCacheEntry(string packageId, Session session)
            : base(() => GetPackage(packageId, session))
        {
        }

        private static async Task<Package?> GetPackage(string packageId, Session session)
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
                    return new Package(packageId, versions, sourceRepository);
                }
            }

            return null;
        }
    }

    private class PackageInfoCacheEntry : CacheEntry<PackageInfo>
    {
        public PackageInfoCacheEntry(PackageIdentity packageIdentity, Session session)
            : base(() => GetPackageInfo(packageIdentity, session))
        {
        }

        private static async Task<PackageInfo?> GetPackageInfo(PackageIdentity packageIdentity, Session session)
        {
            var package = await GetPackageCacheEntry(packageIdentity.Id, session).GetValue().ConfigureAwait(false);

            var sourceRepository = package?.SourceRepository;
            if (sourceRepository is null)
                return null;

            var packageMetadataResource = await sourceRepository
                .GetResourceAsync<PackageMetadataResource>(session.CancellationToken)
                .ConfigureAwait(false);

            var metadata = await packageMetadataResource
                .GetMetadataAsync(packageIdentity, session.SourceCacheContext, NullLogger.Instance, session.CancellationToken)
                .ConfigureAwait(false);

            if (metadata is null)
                return null;

            var versions = package?.Versions ?? Array.Empty<NuGetVersion>();

            return new PackageInfo(packageIdentity)
            {
                Vulnerabilities = metadata.Vulnerabilities?.ToArray(),
                IsDeprecated = await metadata.GetDeprecationMetadataAsync().ConfigureAwait(false) != null,
                IsOutdated = IsOutdated(packageIdentity, versions)
            };
        }
    }

    private class Session : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private ICollection<SourceRepository>? _sourceRepositories;

        public readonly MemoryCache Cache = new(new MemoryCacheOptions { });

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

        public void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            SourceCacheContext.Dispose();
            Cache.Dispose();
        }
    }
}