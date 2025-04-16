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
    public PackageReferenceEntry(string id, VersionRange versionRange, VersionKind versionKind, ProjectItem versionSource, ProjectItemInTargetFramework projectItemInTargetFramework, string justification, bool isPrivateAsset)
    {
        VersionKind = versionKind;
        VersionSource = versionSource;
        ProjectItemInTargetFramework = projectItemInTargetFramework;
        Justification = justification;
        IsPrivateAsset = isPrivateAsset;
        Identity = new(id, versionRange);
    }

    public PackageReference Identity { get; }

    public VersionKind VersionKind { get; }

    public ProjectItem VersionSource { get; }

    public ProjectItemInTargetFramework ProjectItemInTargetFramework { get; }

    public string Justification { get; }

    public bool IsPrivateAsset { get; }
}