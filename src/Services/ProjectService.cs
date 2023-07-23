using Community.VisualStudio.Toolkit;
using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

public static class ProjectService
{
    public static async Task<IReadOnlyCollection<PackageReferenceEntry>> GetPackageReferences()
    {
        var projects = await VS.Solutions.GetAllProjectsAsync().ConfigureAwait(false);

        var projectPaths = projects.Select(project => project.FullPath)
            .ExceptNullItems()
            .ToArray();

        var refTasks = projectPaths.Select(path => Task.Run(() => GetPackageReferences(path)));

        var references = await Task.WhenAll(refTasks).ConfigureAwait(false);

        return references
            .SelectMany(items => items)
            .ToArray();
    }

    internal static IEnumerable<PackageReferenceEntry> GetPackageReferences(string projectPath)
    {
        var items = GetPackageReferenceItems(projectPath);

        var packageReferences = items
            .Select(CreateEntry)
            .ExceptNullItems();

        return packageReferences;
    }

    internal static IEnumerable<ProjectItem> GetPackageReferenceItems(string projectPath)
    {
        var project = new Microsoft.Build.Evaluation.Project(projectPath);

        return project.AllEvaluatedItems.Where(item => item.ItemType == "PackageReference");
    }

    private static PackageReferenceEntry? CreateEntry(ProjectItem projectItem)
    {
        var id = projectItem.EvaluatedInclude;
        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;

        return NuGetVersion.TryParse(versionValue, out var version)
            ? new PackageReferenceEntry(new PackageIdentity(id, version), projectItem)
            : null;
    }
}