using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;
using Project = Microsoft.Build.Evaluation.Project;

namespace NuGetMonitor.Services;

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


    public static NuGetFramework[]? GetTargetFrameworks(this Project project)
    {
        var frameworkNames = (project.GetProperty("TargetFrameworks") ?? project.GetProperty("TargetFramework"))
            ?.EvaluatedValue
            ?.Split(';')
            .Select(value => value.Trim());

        var frameworks = frameworkNames?
            .Select(NuGetFramework.Parse)
            .Distinct()
            .ToArray();

        return frameworks;
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
            lock (projectCollection)
            {
                var project = projectCollection.LoadProject(projectPath);

                return project.AllEvaluatedItems;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Log($"Get package reference item failed: {ex}");

            return Enumerable.Empty<ProjectItem>();
        }
    }

    private static PackageReferenceEntry? CreateEntry(ProjectItem projectItem)
    {
        var id = projectItem.EvaluatedInclude;
        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var versionRange)
            ? new PackageReferenceEntry(id, versionRange, projectItem)
            : null;
    }
}