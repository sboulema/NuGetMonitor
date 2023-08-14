using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;

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

        var projects = await VS.Solutions.GetAllProjectsAsync().ConfigureAwait(false);

        var projectPaths = projects.Select(project => project.FullPath)
            .ExceptNullItems()
            .ToArray();

        return await Task.Run(() =>
        {
            var references = projectPaths.Select(path => GetPackageReferences(projectCollection, path));

            return references
                .SelectMany(items => items)
                .OrderBy(item => item.Identity)
                .ThenBy(item => Path.GetFileName(item.ProjectItem.Xml.ContainingProject.FullPath))
                .ToArray();
        }).ConfigureAwait(false);
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

                return project.AllEvaluatedItems.Where(IsEditablePackageReference);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Log($"Get package reference item failed: {ex}");

            return Enumerable.Empty<ProjectItem>();
        }
    }

    private static bool IsEditablePackageReference(ProjectItem element)
    {
        return IsEditablePackageReference(element.ItemType, element.Metadata.Select(value => new KeyValuePair<string, string?>(value.Name, value.EvaluatedValue)));
    }

    public static bool IsEditablePackageReference(string itemType, IEnumerable<KeyValuePair<string, string?>> metadataEntries)
    {
        return string.Equals(itemType, "PackageReference", StringComparison.OrdinalIgnoreCase)
               && metadataEntries.All(metadata => !string.Equals(metadata.Key, "Condition", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(metadata.Value));
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