using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Models;

public class PackageInfo : IEquatable<PackageInfo>
{
    public PackageInfo(PackageIdentity packageIdentity, Package package, ICollection<PackageVulnerabilityMetadata>? vulnerabilities)
    {
        PackageIdentity = packageIdentity;
        Package = package;
        Vulnerabilities = vulnerabilities;
    }

    public PackageIdentity PackageIdentity { get; }

    public Package Package { get; }

    public ICollection<PackageVulnerabilityMetadata>? Vulnerabilities { get; }

    public bool IsVulnerable => Vulnerabilities?.Count > 0;

    public bool IsDeprecated { get; set; }

    public bool IsOutdated { get; set; }

    public string Issues => string.Join(", ", GetIssues().ExceptNullItems());

    public IReadOnlyCollection<PackageInfo> Dependencies { get; set; } = Array.Empty<PackageInfo>();

    public HashSet<PackageInfo> DependsOn { get; } = new();

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
}