using System.IO;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

public static class ProjectService
{
    private static readonly DelegateEqualityComparer<PackageReferenceEntry> _packageReferenceIdentityComparer = new(item => $"{item?.Identity}|{item?.RelativePath}");

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
        var solutionPath = (await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(true))?.FullPath;

        if (solutionPath.IsNullOrEmpty())
            return Array.Empty<PackageReferenceEntry>();

        var projectCollection = _projectCollection;

        var projects = await VS.Solutions.GetAllProjectsAsync().ConfigureAwait(false);

        var projectPaths = projects.Select(project => project.FullPath)
            .ExceptNullItems()
            .ToArray();

        return await Task.Run(() =>
        {
            var references = projectPaths.Select(path => GetPackageReferences(projectCollection, path, solutionPath));

            return references
                .SelectMany(items => items)
                .Distinct(_packageReferenceIdentityComparer)
                .OrderBy(item => item.Identity)
                .ThenBy(item => item.RelativePath)
                .ToArray();
        }).ConfigureAwait(false);
    }

    internal static IEnumerable<PackageReferenceEntry> GetPackageReferences(ProjectCollection projectCollection, string projectPath, string solutionPath)
    {
        var items = GetPackageReferenceItems(projectCollection, projectPath);

        var packageReferences = items
            .Select(item => CreateEntry(item, solutionPath))
            .ExceptNullItems();

        return packageReferences;
    }

    internal static IEnumerable<ProjectItem> GetPackageReferenceItems(ProjectCollection projectCollection, string projectPath)
    {
        try
        {
            lock (projectCollection)
            {
                var project = projectCollection.LoadProject(projectPath);
                return project.AllEvaluatedItems.Where(IsEditablePackageReference);
            }
        }
        catch
        {
            return Enumerable.Empty<ProjectItem>();
        }
    }

    public static bool IsEditablePackageReference(ProjectItem element)
    {
        return IsEditablePackageReference(element.ItemType, element.Metadata.Select(value => new KeyValuePair<string, string?>(value.Name, value.EvaluatedValue)));
    }

    public static bool IsEditablePackageReference(string itemType, IEnumerable<KeyValuePair<string, string?>> metadataEntries)
    {
        return string.Equals(itemType, "PackageReference", StringComparison.OrdinalIgnoreCase)
               && metadataEntries.All(metadata => !string.Equals(metadata.Key, "Condition", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(metadata.Value));
    }

    private static PackageReferenceEntry? CreateEntry(ProjectItem projectItem, string solutionPath)
    {
        // projectItem.Xml.ContainingProject.f
        var id = projectItem.EvaluatedInclude;
        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;

        return NuGetVersion.TryParse(versionValue, out var version)
            ? new PackageReferenceEntry(new PackageIdentity(id, version), projectItem, GetRelativePath(projectItem, solutionPath))
            : null;
    }

    private static string GetRelativePath(ProjectItem projectItem, string solutionPath)
    {
#nullable disable // Path operations don't have correct nullable annotations in NetFramework.
        var projectFullPath = projectItem.Xml.ContainingProject.FullPath;
        var projectDirectoryUrl = new Uri(Path.GetDirectoryName(projectFullPath), UriKind.Absolute);
        var solutionDirectoryUrl = new Uri(Path.GetDirectoryName(solutionPath), UriKind.Absolute);

        return Path.Combine(solutionDirectoryUrl.MakeRelativeUri(projectDirectoryUrl).ToString().Replace('/', '\\'), Path.GetFileName(projectFullPath));
#nullable enable
    }
}