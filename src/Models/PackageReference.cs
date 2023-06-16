using NuGet.Packaging.Core;
using NuGet.Versioning;
using System.Xml.Linq;

namespace NuGetMonitor.Models
{
    public class PackageReference
    {
        public PackageReference(XElement packageReference)
        {
            PackageIdentity = new PackageIdentity(
                packageReference.Attribute("Include").Value,
                new NuGetVersion(packageReference.Attribute("Version").Value));
        }

        public PackageIdentity PackageIdentity { get; set; }

        public bool IsVulnerable { get; set; }

        public bool IsDeprecated { get; set; }

        public bool IsOutdated { get; set; }
    }
}
