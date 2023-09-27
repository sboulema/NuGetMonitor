using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using Settings = NuGet.Configuration.Settings;

namespace NuGetMonitor.Models
{
    internal sealed class NuGetSession : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public NuGetSession()
        {
            ThrowIfNotOnUIThread();

            var solution = VS.Solutions.GetCurrentSolution();
            var solutionDirectory = Path.GetDirectoryName(solution?.FullPath);

            var settings = Settings.LoadDefaultSettings(solutionDirectory);

            GlobalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);

            var packageSourceProvider = new PackageSourceProvider(settings);
            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
            var sourceRepositories = sourceRepositoryProvider.GetRepositories();

            SourceRepositories = sourceRepositories.Select(item => new RepositoryContext(item)).ToArray();
            PackageDownloadContext = new PackageDownloadContext(SourceCacheContext);
        }

        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());

        public SourceCacheContext SourceCacheContext { get; } = new();

        public PackageDownloadContext PackageDownloadContext { get; }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public ICollection<RepositoryContext> SourceRepositories { get; }

        public string GlobalPackagesFolder { get; private set; }

        public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            SourceCacheContext.Dispose();
            Cache.Dispose();
        }
    }
}