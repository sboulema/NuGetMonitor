using NuGet.Protocol.Core.Types;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Models
{
    internal sealed class RepositoryContext
    {
        private volatile int _accessErrorCounter;

        public RepositoryContext(SourceRepository sourceRepository)
        {
            SourceRepository = sourceRepository;
        }

        public SourceRepository SourceRepository { get; }

        public bool IsAccessible => _accessErrorCounter == 0;

        public bool IsDependencyInfoSupported { get; set; } = true;

        public void AccessError(Exception ex)
        {
            if (Interlocked.Increment(ref _accessErrorCounter) != 1)
                return;

            var packageSource = SourceRepository.PackageSource;
            var exceptionMessage = string.Join("\r\n", ex.ExceptionChain().Select((e, index) => $"  {new string(' ', 2 * index)}- {e.Message}"));
            Log($"Error accessing {packageSource.Name} ({packageSource.SourceUri}):\r\n{exceptionMessage}");
        }
    }
}