using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGetMonitor.Models
{
    public class PackageReference
    {
        public PackageReference(PackageIdentity packageIdentity)
        {
            PackageIdentity = packageIdentity;
        }

        public PackageIdentity PackageIdentity { get; }

        public bool IsVulnerable { get; set; }

        public bool IsDeprecated { get; set; }

        public bool IsOutdated { get; set; }
    }
}
