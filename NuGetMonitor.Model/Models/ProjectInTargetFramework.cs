using System.Collections.ObjectModel;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Model.Models
{
    public sealed class ProjectInTargetFramework
    {
        private static readonly ReadOnlyDictionary<string, ProjectItem> _emptyVersionMap = new(new Dictionary<string, ProjectItem>());
        private static readonly DelegateEqualityComparer<ProjectItem> _itemIncludeComparer = new(item => item?.EvaluatedInclude.ToUpperInvariant());

        public ProjectInTargetFramework(Project project, NuGetFramework targetFramework)
        {
            Project = project;
            TargetFramework = targetFramework;
            CentralVersionMap = GetCentralVersionMap(project);
        }

        public Project Project { get; init; }

        public NuGetFramework TargetFramework { get; init; }

        public ReadOnlyDictionary<string, ProjectItem> CentralVersionMap { get; }

        private static ReadOnlyDictionary<string, ProjectItem> GetCentralVersionMap(Project project)
        {
            var useCentralPackageManagement = project.GetProperty("ManagePackageVersionsCentrally").IsTrue();

            if (!useCentralPackageManagement)
                return _emptyVersionMap;

            var versionMap = project
                .GetItems("PackageVersion")
                .Distinct(_itemIncludeComparer)
                .ToDictionary(item => item.EvaluatedInclude, item => item);

            return new ReadOnlyDictionary<string, ProjectItem>(versionMap);
        }
    }
}