using NuGet.Versioning;
using NuGetMonitor.Services;

namespace NuGetMonitor.Models;

internal sealed record PackageReferenceEntry
{
    public PackageReferenceEntry(string id, VersionRange versionRange, ProjectItemInTargetFramework projectItemInTargetFramework, string justification)
    {
        ProjectItemInTargetFramework = projectItemInTargetFramework;
        Justification = justification;
        Identity = new PackageReference(id, versionRange);
    }

    public PackageReference Identity { get; }

    public ProjectItemInTargetFramework ProjectItemInTargetFramework { get; }

    public string Justification { get; }
}