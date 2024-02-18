using Microsoft.Build.Evaluation;
using NuGet.Versioning;
using NuGetMonitor.Model.Models;

namespace NuGetMonitor.Models;

public sealed record PackageReferenceEntry
{
    public PackageReferenceEntry(string id, VersionRange versionRange, ProjectItem versionSource, ProjectItemInTargetFramework projectItemInTargetFramework, string justification)
    {
        VersionSource = versionSource;
        ProjectItemInTargetFramework = projectItemInTargetFramework;
        Justification = justification;
        Identity = new PackageReference(id, versionRange);
    }

    public PackageReference Identity { get; }

    public ProjectItem VersionSource { get; }

    public ProjectItemInTargetFramework ProjectItemInTargetFramework { get; }

    public string Justification { get; }
}