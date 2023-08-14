using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NuGetMonitor.Models
{
    internal sealed record Package(string Id, ICollection<NuGetVersion> Versions, SourceRepository SourceRepository, NuGetSession Session)
    {
        public override string ToString()
        {
            return string.Join(", ", Versions);
        }
    }
}