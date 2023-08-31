using Microsoft.Build.Evaluation;
using NuGet.Versioning;

namespace NuGetMonitor.Models;

internal sealed record PackageReferenceEntry
{
    public PackageReferenceEntry(string id, VersionRange versionRange, ProjectItem projectItem)
    {
        ProjectItem = projectItem;
        Identity = new PackageReference(id, versionRange);
    }

    public PackageReference Identity { get; }

    public ProjectItem ProjectItem { get; init; }
};