using System.Diagnostics;
using Microsoft.Build.Evaluation;
using NuGet.Versioning;

namespace NuGetMonitor.Model.Models;

public enum VersionKind
{
    Version,
    LocalOverride,
    CentralOverride,
    CentralDefinition
}

[DebuggerDisplay("{Identity}, {ProjectItemInTargetFramework}")]
public sealed record PackageReferenceEntry
{
    public PackageReferenceEntry(string id, VersionRange versionRange, VersionKind versionKind, ProjectItem versionSource, ProjectItemInTargetFramework projectItemInTargetFramework, bool isPrivateAsset)
    {
        VersionKind = versionKind;
        VersionSource = versionSource;
        ProjectItemInTargetFramework = projectItemInTargetFramework;
        Justification = versionSource.GetJustification();
        IsPrivateAsset = isPrivateAsset;
        IsPinned = versionSource.GetIsPinned();
        Identity = new(id, versionRange, IsPinned);
    }

    public PackageReference Identity { get; }

    public VersionKind VersionKind { get; }

    public ProjectItem VersionSource { get; }

    public ProjectItemInTargetFramework ProjectItemInTargetFramework { get; }

    public string Justification { get; }

    public bool IsPrivateAsset { get; }

    public bool IsPinned { get; }
}