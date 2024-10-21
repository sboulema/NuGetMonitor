using NuGet.Versioning;

namespace NuGetMonitor.Model.Models;

public sealed record Package(string Id, ICollection<NuGetVersion> Versions, RepositoryContext RepositoryContext)
{
    public override string ToString()
    {
        return string.Join(", ", Versions);
    }
}