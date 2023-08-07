using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NuGetMonitor.Models
{
    public record Package(string Id, ICollection<NuGetVersion> Versions, SourceRepository SourceRepository, NugetSession Session)
    {
        public override string ToString()
        {
            return string.Join(", ", Versions);
        }
    }
}