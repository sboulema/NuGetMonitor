using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;
using Project = Microsoft.Build.Evaluation.Project;

namespace NuGetMonitor.Services;

internal sealed record ProjectInTargetFramework(Project Project, NuGetFramework TargetFramework);

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
                .ThenBy(item => Path.GetFileName(item.ProjectItem.Xml.ContainingProject.FullPath))
                .ToArray();
        });
    }

    public static ProjectInTargetFramework[] GetProjectsInTargetFramework(this Project project)
    {
        var frameworkNames = (project.GetProperty("TargetFrameworks") ?? project.GetProperty("TargetFramework"))
            ?.EvaluatedValue
            ?.Split(';')
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

        lock (projectCollection)
        {
            return frameworks
                .Select(framework => LoadProjectInTargetFramework(project, framework, projectCollection))
                .ToArray();
        }
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

    private static IEnumerable<ProjectItem> GetPackageReferenceItems(ProjectCollection projectCollection, string projectPath)
    {
        try
        {
            Project project;

            lock (projectCollection)
            {
                project = projectCollection.LoadProject(projectPath);
            }

            var projects = project.GetProjectsInTargetFramework();

            var allItems = projects.SelectMany(p => p.Project.GetItems("PackageReference"));

            return allItems;
        }
        catch (Exception ex)
        {
            Log($"Get package reference item failed: {ex}");

            return Enumerable.Empty<ProjectItem>();
        }
    }

    private static PackageReferenceEntry? CreateEntry(ProjectItem projectItem)
    {
        var id = projectItem.EvaluatedInclude;

        // Ignore the implicit NetStandard library reference in projects targeting NetStandard.
        if (id.Equals(NetStandardPackageId, StringComparison.OrdinalIgnoreCase))
            return null;

        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var versionRange)
            ? new PackageReferenceEntry(id, versionRange, projectItem, projectItem.GetMetadataValue("Justification"))
            : null;

    }
}