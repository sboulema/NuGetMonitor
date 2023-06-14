using Community.VisualStudio.Toolkit;
using NuGetMonitor.Models;
using System;
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
        public static async Task<IEnumerable<string>> GetProjectPaths()
        {
            var projects = await VS.Solutions.GetAllProjectsAsync();
            var projectPaths = projects.Select(project => project.FullPath);

            return projectPaths;
        }

        public static IEnumerable<PackageReference> GetPackageReferences(string projectPath)
        {
            var xml = File.ReadAllText(projectPath);
            var doc = XDocument.Parse(xml);

            var packageReferences = doc
                .XPathSelectElements("//PackageReference")
                .Select(packageReference => new PackageReference
                {
                    Include = packageReference.Attribute("Include").Value,
                    Version = new Version(packageReference.Attribute("Version").Value)
                });

            return packageReferences;
        }
    }
}
