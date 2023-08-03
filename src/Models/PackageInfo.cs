using NuGet.Packaging.Core;
using NuGet.Protocol;

namespace NuGetMonitor.Models;

public record PackageInfo(PackageIdentity PackageIdentity)
{
    public ICollection<PackageVulnerabilityMetadata>? Vulnerabilities { get; set; }

    public bool IsVulnerable => Vulnerabilities?.Count > 0;

    public bool IsDeprecated { get; set; }

    public bool IsOutdated { get; set; }

    public string Issues => string.Join(", ", GetIssues());

    private IEnumerable<string> GetIssues()
    {
        if (IsDeprecated) 
            yield return "Deprecated";

        if (Vulnerabilities?.Count > 0)
            yield return $"{Vulnerabilities.Count} vulnerabilities";
    }
}