﻿using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Model.Services;
using NuGetMonitor.Models;
using TomsToolbox.Essentials;
using Project = Microsoft.Build.Evaluation.Project;
using ProjectItem = Microsoft.Build.Evaluation.ProjectItem;

namespace NuGetMonitor.Services;

public static class ProjectService
{
    private static readonly string[] _allAssets = new[] { "runtime", "build", "native", "contentfiles", "analyzers", "buildtransitive" }.OrderBy(i => i).ToArray();

    static ProjectCollection _projectCollection = new();

    public static void ClearCache()
    {
        Interlocked.Exchange(ref _projectCollection, new ProjectCollection()).Dispose();
    }

    public static async Task<IReadOnlyCollection<PackageReferenceEntry>> GetPackageReferences(ICollection<string> projectFolders)
    {
        var projectCollection = _projectCollection;

        return await Task.Run(() =>
        {
            var references = projectFolders.Select(path => GetPackageReferences(projectCollection, path));

            return references
                .SelectMany(items => items)
                .OrderBy(item => item.Identity.Id)
                .ThenBy(item => Path.GetFileName(item.VersionSource.GetContainingProject().FullPath))
                .ToArray();
        });
    }

    public static int NormalizePackageReferences(IEnumerable<ProjectItem> projectItems)
    {
        using var projectCollection = new ProjectCollection();
        var numberOfUpdatedItems = 0;

        var projectFiles = projectItems
            .Select(i => i.GetContainingProject().FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var projectFile in projectFiles)
        {
            var isDirty = false;

            var project = ProjectRootElement.Open(projectFile, projectCollection, true);

            var itemElements = project.Items
                .Where(item => item.ItemType == "PackageReference");

            foreach (var itemElement in itemElements)
            {
                var metadata = itemElement.Metadata;

                var metadataElements = metadata.Where(meta => !meta.ExpressedAsAttribute).ToArray();
                if (metadataElements.Length == 0)
                    continue;

                foreach (var metadataElement in metadataElements)
                {
                    metadataElement.ExpressedAsAttribute = true;
                }

                NormalizeIncludeAssets(metadata);

                numberOfUpdatedItems += 1;
                isDirty = true;
            }

            if (isDirty)
            {
                project.Save();
            }
        }

        return numberOfUpdatedItems;
    }

    private static void NormalizeIncludeAssets(IEnumerable<ProjectMetadataElement> metadata)
    {
        var includeAssetsElement = metadata.FirstOrDefault(meta => meta.Name == "IncludeAssets");
        if (includeAssetsElement is null)
            return;

        var includeAssets = includeAssetsElement.Value;
        if (string.IsNullOrEmpty(includeAssets))
            return;

        var parts = includeAssets.Split(';')
            .Select(s => s.Trim())
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

        if (!_allAssets.SequenceEqual(parts, StringComparer.OrdinalIgnoreCase))
            return;

        includeAssetsElement.Value = string.Empty;
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
            Log(LogLevel.Error, $"Get package reference item failed: {ex}");

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

        var version = projectItem.GetVersion() ?? projectItem.GetVersionOverride();
        var project = projectItemInTargetFramework.Project;

        if (version is null && project.CentralVersionMap.TryGetValue(id, out versionSource))
        {
            version = versionSource.GetVersion();
        }

        // ! versionSource is checked above.
        return version is null
            ? null
            : new PackageReferenceEntry(id, version, versionSource!, projectItemInTargetFramework, projectItem.GetMetadataValue("Justification"));
    }

    internal static bool IsTrue(this ProjectProperty? property)
    {
        return "true".Equals(property?.EvaluatedValue, StringComparison.OrdinalIgnoreCase);
    }

    internal static VersionRange? GetVersion(this ProjectItem projectItem)
    {
        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var version) ? version : null;
    }

    internal static VersionRange? GetVersionOverride(this ProjectItem projectItem)
    {
        var versionValue = projectItem.GetMetadata("VersionOverride")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var version) ? version : null;
    }

    public static ProjectRootElement GetContainingProject(this ProjectItem projectItem)
    {
        return projectItem.Xml.ContainingProject;
    }
}