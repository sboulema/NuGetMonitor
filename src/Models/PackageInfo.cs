using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Models;

public record PackageInfo(PackageIdentity PackageIdentity, Package Package, ICollection<PackageVulnerabilityMetadata>? Vulnerabilities)
{
    public bool IsVulnerable => Vulnerabilities?.Count > 0;

    public bool IsDeprecated { get; set; }

    public bool IsOutdated { get; set; }

    public string Issues => string.Join(", ", GetIssues().ExceptNullItems());

    private IEnumerable<string?> GetIssues()
    {
        if (IsDeprecated) 
            yield return "Deprecated";

        yield return Vulnerabilities?.CountedDescription("vulnerability");
    }
}