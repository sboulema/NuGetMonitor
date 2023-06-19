using Community.VisualStudio.Toolkit;
using NuGetMonitor.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Packaging.Core;


namespace NuGetMonitor.Services
{
    public static class ProjectService
    {
        public static async Task<IReadOnlyCollection<PackageIdentity>> GetPackageReferences()
        {
            var projects = await VS.Solutions.GetAllProjectsAsync().ConfigureAwait(false);

            var projectPaths = projects.Select(project => project.FullPath).ToArray();

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
                .Select(PackageReference.CreateIdentity)
                .Where(item => item != null);

            return packageReferences;
        }
    }
}
