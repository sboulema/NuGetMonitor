using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGetMonitor.Models
{
    public class PackageReference
    {
        private PackageReference(PackageIdentity packageIdentity)
        {
            PackageIdentity = packageIdentity;
        }

        public PackageIdentity PackageIdentity { get; }

        public bool IsVulnerable { get; set; }

        public bool IsDeprecated { get; set; }

        public bool IsOutdated { get; set; }

        public static PackageReference Create(Microsoft.Build.Evaluation.ProjectItem projectItem)
        {
            var id = projectItem.EvaluatedInclude;
            var versionValue = projectItem.GetMetadata("Version")?.EvaluatedValue;

            if (!NuGetVersion.TryParse(versionValue, out var version))
                return null;

            return new PackageReference(new PackageIdentity(id, version));
        }
    }
}
