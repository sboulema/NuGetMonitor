using Community.VisualStudio.Toolkit;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using TomsToolbox.Essentials;


namespace NuGetMonitor.Services
{
    public static class ProjectService
    {
        public static async Task<IReadOnlyCollection<PackageIdentity>> GetPackageReferences()
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

        private static IEnumerable<PackageIdentity> GetPackageReferences(string projectPath)
        {
            var project = new Microsoft.Build.Evaluation.Project(projectPath);

            var items = project.AllEvaluatedItems.Where(item => item.ItemType == "PackageReference");

            var packageReferences = items
                .Select(CreateIdentity)
                .ExceptNullItems();

            return packageReferences;
        }

        private static PackageIdentity? CreateIdentity(Microsoft.Build.Evaluation.ProjectItem projectItem)
        {
            var id = projectItem.EvaluatedInclude;
            var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;

            if (!NuGetVersion.TryParse(versionValue, out var version))
            {
                return null;
            }

            return new PackageIdentity(id, version);
        }
    }
}
