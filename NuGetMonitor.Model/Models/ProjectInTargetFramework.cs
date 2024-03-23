using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGetMonitor.Model.Services;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Model.Models;

[DebuggerDisplay("Project: {Name}, Framework: {TargetFramework}")]
public sealed class ProjectInTargetFramework
{
    private static readonly ReadOnlyDictionary<string, ProjectItem> _emptyVersionMap = new(new Dictionary<string, ProjectItem>());
    private static readonly DelegateEqualityComparer<ProjectItem> _itemIncludeComparer = new(item => item?.EvaluatedInclude.ToUpperInvariant());

    public ProjectInTargetFramework(Project project, NuGetFramework targetFramework)
    {
        Project = project;
        TargetFramework = targetFramework;
        CentralVersionMap = GetCentralVersionMap(project);
    }

    public Project Project { get; init; }

    public NuGetFramework TargetFramework { get; init; }

    public ReadOnlyDictionary<string, ProjectItem> CentralVersionMap { get; }

    public string Name => Path.GetFileName(Project.FullPath);

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

        return new ReadOnlyDictionary<string, ProjectItem>(versionMap);
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
}

public record ProjectInTargetFrameworkWithReferenceEntries(ProjectInTargetFramework Project, IReadOnlyCollection<PackageReferenceEntry> PackageReferences);
