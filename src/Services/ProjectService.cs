using System.Collections.ObjectModel;
using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;
using Project = Microsoft.Build.Evaluation.Project;
using ProjectItem = Microsoft.Build.Evaluation.ProjectItem;

namespace NuGetMonitor.Services;

internal sealed class ProjectItemInTargetFramework
{
    public ProjectItemInTargetFramework(ProjectItem projectItem, ProjectInTargetFramework project)
    {
        ProjectItem = projectItem;
        Project = project;
    }

    public ProjectItem ProjectItem { get; init; }

    public NuGetFramework TargetFramework => Project.TargetFramework;

    public ProjectInTargetFramework Project { get; }
}

internal sealed class ProjectInTargetFramework
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
}

internal static class ProjectService
{
    private static ProjectCollection _projectCollection = new();

    static ProjectService()
    {
        VS.Events.SolutionEvents.OnAfterCloseSolution += ClearCache;
    }

    public static void ClearCache()
    {
        Interlocked.Exchange(ref _projectCollection, new ProjectCollection()).Dispose();
    }

    public static async Task<IReadOnlyCollection<PackageReferenceEntry>> GetPackageReferences()
    {
        var projectCollection = _projectCollection;

        var projects = await VS.Solutions.GetAllProjectsAsync();

        var projectPaths = projects.Select(project => project.FullPath)
            .ExceptNullItems()
            .ToArray();

        return await Task.Run(() =>
        {
            var references = projectPaths.Select(path => GetPackageReferences(projectCollection, path));

            return references
                .SelectMany(items => items)
                .OrderBy(item => item.Identity.Id)
                .ThenBy(item => Path.GetFileName(item.VersionSource.GetContainingProject().FullPath))
                .ToArray();
        });
    }

    public static ProjectInTargetFramework[] GetProjectsInTargetFramework(this Project project)
    {
        var frameworkNames = (project.GetProperty("TargetFrameworks") ?? project.GetProperty("TargetFramework"))
            ?.EvaluatedValue
            .Split(';')
            .Select(value => value.Trim())
            .ToArray();

        if (frameworkNames is null || frameworkNames.Length == 0)
            return new[] { new ProjectInTargetFramework(project, NuGetFramework.AgnosticFramework) };

        var frameworks = frameworkNames
            .Select(NuGetFramework.Parse)
            .Distinct()
            .ToArray();

        if (frameworks.Length == 1)
            return new[] { new ProjectInTargetFramework(project, frameworks[0]) };

        var projectCollection = _projectCollection;

        return frameworks
            .Select(framework => LoadProjectInTargetFramework(project, framework, projectCollection))
            .ToArray();
    }

    private static ProjectInTargetFramework LoadProjectInTargetFramework(Project project, NuGetFramework framework, ProjectCollection projectCollection)
    {
        var properties = new Dictionary<string, string>
        {
            { "TargetFramework", framework.GetShortFolderName() }
        };

        var specificProject = projectCollection.LoadProject(project.FullPath, properties, null);

        return new ProjectInTargetFramework(specificProject, framework);
    }

    private static IEnumerable<PackageReferenceEntry> GetPackageReferences(ProjectCollection projectCollection, string projectPath)
    {
        var items = GetPackageReferenceItems(projectCollection, projectPath);

        var packageReferences = items
            .Select(CreateEntry)
            .ExceptNullItems();

        return packageReferences;
    }

    private static IEnumerable<ProjectItemInTargetFramework> GetPackageReferenceItems(ProjectCollection projectCollection, string projectPath)
    {
        try
        {
            var project = projectCollection.LoadProject(projectPath);

            var frameworkSpecificProjects = project.GetProjectsInTargetFramework();

            var allItems = frameworkSpecificProjects.SelectMany(GetPackageReferenceItems);

            return allItems;
        }
        catch (Exception ex)
        {
            Log($"Get package reference item failed: {ex}");

            return Enumerable.Empty<ProjectItemInTargetFramework>();
        }
    }

    private static IEnumerable<ProjectItemInTargetFramework> GetPackageReferenceItems(ProjectInTargetFramework frameworkSpecificProject)
    {
        var project = frameworkSpecificProject.Project;

        return project.GetItems("PackageReference")
            .Select(item => new ProjectItemInTargetFramework(item, frameworkSpecificProject));
    }

    private static PackageReferenceEntry? CreateEntry(ProjectItemInTargetFramework projectItemInTargetFramework)
    {
        var projectItem = projectItemInTargetFramework.ProjectItem;
        var versionSource = projectItem;

        var id = projectItem.EvaluatedInclude;

        // Ignore the implicit NetStandard library reference in projects targeting NetStandard.
        if (id.Equals(NetStandardPackageId, StringComparison.OrdinalIgnoreCase))
            return null;

        var version = projectItem.GetVersion();
        var project = projectItemInTargetFramework.Project;

        if (version is null && project.CentralVersionMap.TryGetValue(id, out versionSource))
        {
            version = versionSource.GetVersion();
        }

        return version is null
            ? null
            : new PackageReferenceEntry(id, version, versionSource, projectItemInTargetFramework, projectItem.GetMetadataValue("Justification"));
    }

    internal static bool IsTrue(this ProjectProperty property)
    {
        return "true".Equals(property.EvaluatedValue, StringComparison.OrdinalIgnoreCase);
    }

    internal static VersionRange? GetVersion(this ProjectItem projectItem)
    {
        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var version) ? version : null;
    }

    internal static ProjectRootElement GetContainingProject(this ProjectItem projectItem)
    {
        return projectItem.Xml.ContainingProject;
    }
}