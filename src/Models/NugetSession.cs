using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using Settings = NuGet.Configuration.Settings;

namespace NuGetMonitor.Models
{
    public sealed class NugetSession : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly TaskCompletionSource<ICollection<SourceRepository>> _sourceRepositories = new();

        public NugetSession()
        {
            Load();
        }

        public MemoryCache Cache { get; } = new(new MemoryCacheOptions { });

        public SourceCacheContext SourceCacheContext { get; } = new();

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public Task<ICollection<SourceRepository>> GetSourceRepositories() => _sourceRepositories.Task;

        public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            SourceCacheContext.Dispose();
            Cache.Dispose();
        }

        private async void Load()
        {
            var solution = await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(false);
            var solutionDirectory = Path.GetDirectoryName(solution?.FullPath);

            var packageSourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(solutionDirectory));
            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());
            var sourceRepositories = sourceRepositoryProvider.GetRepositories();

            _sourceRepositories.SetResult(sourceRepositories.ToArray());
        }
    }
}