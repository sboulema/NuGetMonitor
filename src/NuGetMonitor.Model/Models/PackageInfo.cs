using System.Text;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Model.Models;

public sealed class PackageInfo
{
    public PackageInfo(PackageIdentity packageIdentity, Package package, NuGetSession session, ICollection<PackageVulnerabilityMetadata>? vulnerabilities, PackageDeprecationMetadata? deprecationMetadata, Uri projectUrl)
    {
        PackageIdentity = packageIdentity;
        Package = package;
        Session = session;
        Vulnerabilities = vulnerabilities;
        DeprecationMetadata = deprecationMetadata;
        ProjectUrl = projectUrl;
    }

    public PackageIdentity PackageIdentity { get; }

    public Package Package { get; }

    public NuGetSession Session { get; }

    public ICollection<PackageVulnerabilityMetadata>? Vulnerabilities { get; }

    public PackageDeprecationMetadata? DeprecationMetadata { get; }

    public bool IsVulnerable => Vulnerabilities?.Count > 0;

    public bool IsDeprecated => DeprecationMetadata != null;

    public bool IsOutdated { get; init; }

    public string? VulnerabilityMitigation { get; set; }

    public string Issues => string.Join(", ", GetIssues().ExceptNullItems());

    public bool HasIssues => IsDeprecated || (IsVulnerable && VulnerabilityMitigation.IsNullOrEmpty());

    public Uri ProjectUrl { get; }

    public bool IsPinned { get; set; }

    private IEnumerable<string?> GetIssues()
    {
        if (IsDeprecated)
            yield return "Deprecated";

        yield return Vulnerabilities?.CountedDescription("vulnerability");
    }

    public void AppendIssueDetails(StringBuilder text)
    {
#pragma warning disable CA1305 // Specify IFormatProvider => Not available in NetFramework

        if (!HasIssues)
            return;

        text.AppendLine(PackageIdentity.ToString());

        if (DeprecationMetadata is not null)
        {
            text.AppendLine($"""- Deprecation: "{DeprecationMetadata.Message}", reasons: "{string.Join(", ", DeprecationMetadata.Reasons)}", alternate: "{DeprecationMetadata.AlternatePackage}".""");
        }

        if (Vulnerabilities is null)
            return;

        text.AppendLine("- Vulnerabilities:");
        foreach (var vulnerability in Vulnerabilities)
        {
            text.AppendLine($"""  - Severity: {vulnerability.Severity}, "{vulnerability.AdvisoryUrl}".""");
        }
#pragma warning restore CA1305 // Specify IFormatProvider
    }

    public override string ToString()
    {
        return PackageIdentity.ToString();
    }
}