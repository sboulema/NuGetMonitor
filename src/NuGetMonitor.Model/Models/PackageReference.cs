using System.Diagnostics;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGetMonitor.Model.Models;

[DebuggerDisplay("{Id}, {VersionRange}")]
public sealed record PackageReference(string Id, VersionRange VersionRange)
{
    public PackageIdentity? FindBestMatch(IEnumerable<NuGetVersion>? versions)
    {
        if (NuGetVersion.TryParse(VersionRange.OriginalString, out var simpleVersion))
            return new(Id, simpleVersion);

        var version = VersionRange.FindBestMatch(versions);

        return version is null ? null : new PackageIdentity(Id, version);
    }
}