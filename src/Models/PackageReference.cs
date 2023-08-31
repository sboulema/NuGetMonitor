using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGetMonitor.Models;

internal sealed record PackageReference(string Id, VersionRange VersionRange)
{
    public PackageIdentity? FindBestMatch(IEnumerable<NuGetVersion>? versions)
    {
        if (NuGetVersion.TryParse(VersionRange.OriginalString, out var simpleVersion))
            return new PackageIdentity(Id, simpleVersion);

        var version = VersionRange.FindBestMatch(versions);

        return version is null ? null : new PackageIdentity(Id, version);
    }
}