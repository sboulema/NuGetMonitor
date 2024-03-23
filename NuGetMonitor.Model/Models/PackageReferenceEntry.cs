using Microsoft.Build.Evaluation;
using NuGet.Versioning;

namespace NuGetMonitor.Model.Models;

public sealed record PackageReferenceEntry
{
    public PackageReferenceEntry(string id, VersionRange versionRange, ProjectItem versionSource, ProjectItemInTargetFramework projectItemInTargetFramework, string justification, bool isPrivateAsset)
    {
        VersionSource = versionSource;
        ProjectItemInTargetFramework = projectItemInTargetFramework;
        Justification = justification;
        IsPrivateAsset = isPrivateAsset;
        Identity = new PackageReference(id, versionRange);
    }

    public PackageReference Identity { get; }

    public ProjectItem VersionSource { get; }

    public ProjectItemInTargetFramework ProjectItemInTargetFramework { get; }

    public string Justification { get; }

    public bool IsPrivateAsset { get; }
}