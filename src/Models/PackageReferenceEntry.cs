using Microsoft.Build.Evaluation;
using NuGet.Versioning;

namespace NuGetMonitor.Models;

internal sealed record PackageReferenceEntry
{
    public PackageReferenceEntry(string id, VersionRange versionRange, ProjectItem projectItem, string justification)
    {
        ProjectItem = projectItem;
        Justification = justification;
        Identity = new PackageReference(id, versionRange);
    }

    public PackageReference Identity { get; }

    public ProjectItem ProjectItem { get; }

    public string Justification { get; }
}