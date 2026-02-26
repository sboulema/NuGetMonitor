using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetMonitor.Model.Models;
using TomsToolbox.Essentials;
using Project = Microsoft.Build.Evaluation.Project;
using ProjectItem = Microsoft.Build.Evaluation.ProjectItem;

namespace NuGetMonitor.Model.Services;

public static class ProjectService
{
    private static readonly string[] _allAssets = new[] { "runtime", "build", "native", "contentfiles", "analyzers", "buildtransitive" }.OrderBy(i => i).ToArray();

    static ProjectCollection _projectCollection = new();

    public static void ClearCache()
    {
        Interlocked.Exchange(ref _projectCollection, new()).Dispose();
    }

    public static async Task<IReadOnlyCollection<PackageReferenceEntry>> GetPackageReferences(ICollection<string> projectFolders)
    {
        var projectCollection = _projectCollection;

        return await Task.Run(() =>
        {
            var references = projectFolders.Select(projectPath => GetPackageReferences(projectCollection, projectPath));

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

    private static ProjectInTargetFramework[] GetProjectsInTargetFramework(this Project project)
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

        return new(specificProject, framework);
    }

    private static IEnumerable<PackageReferenceEntry> GetPackageReferences(ProjectCollection projectCollection, string projectPath)
    {
        var items = GetPackageReferenceItems(projectCollection, projectPath);

        var packageReferences = items
            .Select(CreatePackageReferenceEntry)
            .ExceptNullItems();

        return packageReferences;
    }

    private static IEnumerable<ProjectItemInTargetFramework> GetPackageReferenceItems(ProjectCollection projectCollection, string projectPath)
    {
        try
        {
            var project = projectCollection.LoadProject(projectPath);

            var frameworkSpecificProjects = project.GetProjectsInTargetFramework();

            return frameworkSpecificProjects.SelectMany(GetPackageReferenceItems);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Get package reference item failed: {ex}");

            return [];
        }
    }

    private static IEnumerable<ProjectItemInTargetFramework> GetPackageReferenceItems(ProjectInTargetFramework frameworkSpecificProject)
    {
        var project = frameworkSpecificProject.Project;

        var references = project.GetItems("PackageReference").AsEnumerable();

        if (frameworkSpecificProject.IsCentralVersionManagementEnabled)
        {
            references = references.Concat(project.GetItems("GlobalPackageReference"));
        }

        // ignore synthetic references, e.g. generated from GlobalPackageReference
        references = references.Where(item => item.Xml.Include == item.EvaluatedInclude);

        return references.Select(item => new ProjectItemInTargetFramework(item, frameworkSpecificProject));
    }

    private static PackageReferenceEntry? CreatePackageReferenceEntry(ProjectItemInTargetFramework projectItemInTargetFramework)
    {
        var projectItem = projectItemInTargetFramework.ProjectItem;
        var versionSource = projectItem;

        var id = projectItem.EvaluatedInclude;

        // Ignore the implicit NetStandard library reference in projects targeting NetStandard.
        if (id.Equals(NetStandardPackageId, StringComparison.OrdinalIgnoreCase))
            return null;

        var project = projectItemInTargetFramework.Project;

        var versionKind = VersionKind.Version;
        var version = projectItem.GetVersion();
        if (version is null)
        {
            version = projectItem.GetVersionOverride();

            if (version is not null)
            {
                versionKind = VersionKind.LocalOverride;
            }
            else if (project.CentralVersionMap.TryGetValue(id, out var mappedVersion))
            {
                versionSource = mappedVersion;
                version = versionSource.GetVersion();
                versionKind = VersionKind.CentralOverride;
            }
        }

        if (version is null)
            return null;

        return new(id, version, versionKind, versionSource, projectItemInTargetFramework, projectItem.GetIsPrivateAsset());
    }

    internal static bool IsTrue(this ProjectProperty? property)
    {
        return "true".Equals(property?.EvaluatedValue, StringComparison.OrdinalIgnoreCase);
    }

    public static VersionRange? GetVersion(this ProjectItem projectItem)
    {
        var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var version) ? version : null;
    }

    public static VersionRange? GetVersionOverride(this ProjectItem projectItem)
    {
        var versionValue = projectItem.GetMetadata("VersionOverride")?.EvaluatedValue;
        if (versionValue.IsNullOrEmpty())
            return null;

        return VersionRange.TryParse(versionValue, out var version) ? version : null;
    }

    internal static bool GetIsPrivateAsset(this ProjectItem projectItem)
    {
        if (projectItem.IsGlobalPackageReference())
            return true;

        var value = projectItem.GetMetadata("PrivateAssets")?.EvaluatedValue;

        return string.Equals(value, "all", StringComparison.OrdinalIgnoreCase);
    }

    public static ProjectRootElement GetContainingProject(this ProjectItem projectItem)
    {
        return projectItem.Xml.ContainingProject;
    }
}