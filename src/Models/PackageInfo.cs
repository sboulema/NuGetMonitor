using NuGet.Packaging.Core;

namespace NuGetMonitor.Models;

public record PackageInfo(PackageIdentity PackageIdentity)
{
    public bool IsVulnerable { get; set; }

    public bool IsDeprecated { get; set; }

    public bool IsOutdated { get; set; }
}