using Community.VisualStudio.Toolkit;
using NuGetMonitor.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;


namespace NuGetMonitor.Services
{
    public static class ProjectService
    {
        public static async Task<IEnumerable<PackageReference>> GetPackageReferences()
        {
            var projects = await VS.Solutions.GetAllProjectsAsync();

            return projects
                .Select(project => project.FullPath)
                .ToList()
                .SelectMany(path => GetPackageReferences(path));
        }

        private static IEnumerable<PackageReference> GetPackageReferences(string projectPath)
        {
            var xml = File.ReadAllText(projectPath);
            var doc = XDocument.Parse(xml);

            var packageReferences = doc
                .XPathSelectElements("//PackageReference")
                .Select(packageReference => new PackageReference(packageReference));

            return packageReferences;
        }
    }
}
