using System.Text;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Models;

internal sealed class PackageInfo : IEquatable<PackageInfo>
{
    public PackageInfo(PackageIdentity packageIdentity, Package package, NuGetSession session, ICollection<PackageVulnerabilityMetadata>? vulnerabilities, PackageDeprecationMetadata? deprecationMetadata)
    {
        PackageIdentity = packageIdentity;
        Package = package;
        Session = session;
        Vulnerabilities = vulnerabilities;
        DeprecationMetadata = deprecationMetadata;
    }

    public PackageIdentity PackageIdentity { get; }

    public Package Package { get; }

    public NuGetSession Session { get; }

    public ICollection<PackageVulnerabilityMetadata>? Vulnerabilities { get; }

    public PackageDeprecationMetadata? DeprecationMetadata { get; }

    public bool IsVulnerable => Vulnerabilities?.Count > 0;

    public bool IsDeprecated => DeprecationMetadata != null;

    public bool IsOutdated { get; init; }

    public string Issues => string.Join(", ", GetIssues().ExceptNullItems());

    public bool HasIssues => IsDeprecated || IsVulnerable;

    private IEnumerable<string?> GetIssues()
    {
        if (IsDeprecated)
            yield return "Deprecated";

        yield return Vulnerabilities?.CountedDescription("vulnerability");
    }

    public bool Equals(PackageInfo? other)
    {
        if (other is null)
            return false;

        return ReferenceEquals(this, other) || PackageIdentity.Equals(other.PackageIdentity);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as PackageInfo);
    }

    public override int GetHashCode()
    {
        return PackageIdentity.GetHashCode();
    }

    public void AppendIssueDetails(StringBuilder text)
    {
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
    }

    public override string ToString()
    {
        return PackageIdentity.ToString();
    }
}