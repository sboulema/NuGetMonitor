using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Model.Services;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Model.Models;

[DebuggerDisplay("Project: {Name}, Framework: {TargetFramework}")]
public sealed class ProjectInTargetFramework : IEquatable<ProjectInTargetFramework>
{
    private static readonly ReadOnlyDictionary<string, ProjectItem> _emptyVersionMap = new(new Dictionary<string, ProjectItem>());
    private static readonly DelegateEqualityComparer<ProjectItem> _itemIncludeComparer = new(item => item?.EvaluatedInclude.ToUpperInvariant());

    public ProjectInTargetFramework(Project project, NuGetFramework targetFramework)
    {
        Project = project;
        TargetFramework = targetFramework;
        CentralVersionMap = GetCentralVersionMap(project);
        IsTransitivePinningEnabled = IsCentralVersionManagementEnabled && project.GetProperty("CentralPackageTransitivePinningEnabled").IsTrue();
        PackageMitigations = GetPackageMitigations(project);
    }

    public Project Project { get; init; }

    public NuGetFramework TargetFramework { get; init; }

    public IReadOnlyDictionary<string, ProjectItem> CentralVersionMap { get; }

    public IReadOnlyDictionary<PackageIdentity, string> PackageMitigations { get; }

    public bool IsCentralVersionManagementEnabled => CentralVersionMap.Count > 0;

    public bool IsTransitivePinningEnabled { get; }

    public string Name => Path.GetFileName(Project.FullPath);

    public string NameAndFramework => $"{Name} ({TargetFramework})";

    public IEnumerable<ProjectInTargetFramework> GetReferencedProjects(IEnumerable<ProjectInTargetFramework> allProjects)
    {
        return Project.GetItems("ProjectReference")
            .Select(item => item.EvaluatedInclude)
            .Select(path => Path.GetFullPath(Path.Combine(Project.DirectoryPath, path)))
            .Select(path => GetBestMatch(allProjects, path))
            .ExceptNullItems();
    }

    private static ReadOnlyDictionary<string, ProjectItem> GetCentralVersionMap(Project project)
    {
        var useCentralPackageManagement = project.GetProperty("ManagePackageVersionsCentrally").IsTrue();

        if (!useCentralPackageManagement)
            return _emptyVersionMap;

        var versionMap = project
            .GetItems("PackageVersion")
            .Distinct(_itemIncludeComparer)
            .ToDictionary(item => item.EvaluatedInclude, item => item);

        return new(versionMap);
    }

    private static ReadOnlyDictionary<PackageIdentity, string> GetPackageMitigations(Project project)
    {
        var projectItems = project
            .GetItems("PackageMitigation");

        var mitigations = projectItems
            .Select(item => new
            {
                Identity = new PackageIdentity(item.EvaluatedInclude, NuGetVersion.Parse(item.GetMetadataValue("Version"))),
                Justification = item.GetJustification()
            }
            )
            .ToDictionary(item => item.Identity, item => item.Justification);

        return new(mitigations);
    }

    private ProjectInTargetFramework? GetBestMatch(IEnumerable<ProjectInTargetFramework> projects, string projectPath)
    {
        var candidates = projects
            .Where(project => string.Equals(project.Project.FullPath, projectPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => NuGetFrameworkUtility.GetNearest(candidates, TargetFramework, item => item.TargetFramework) ?? candidates[0]
        };
    }

    public bool Equals(ProjectInTargetFramework? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Project.Equals(other.Project) && TargetFramework.Equals(other.TargetFramework);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ProjectInTargetFramework);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Project.GetHashCode() * 397) ^ TargetFramework.GetHashCode();
        }
    }
}