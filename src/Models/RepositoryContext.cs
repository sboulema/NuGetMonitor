using NuGet.Protocol.Core.Types;

namespace NuGetMonitor.Models
{
    internal class RepositoryContext
    {
        public RepositoryContext(SourceRepository sourceRepository)
        {
            SourceRepository = sourceRepository;
        }

        public SourceRepository SourceRepository { get; }

        public bool IsAccessible { get; set; } = true;

        public bool IsDependencyInfoSupported { get; set; } = true;
    }
}