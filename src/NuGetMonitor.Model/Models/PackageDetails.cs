using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace NuGetMonitor.Model.Models
{
    public sealed record PackageDetails(
        PackageIdentity Identity,
        IReadOnlyCollection<PackageDependencyGroup> DependencyGroups,
        IReadOnlyCollection<NuGetFramework> SupportedFrameworks,
        string? RepositoryUrl)
    {
        public static PackageDetails CreateEmpty(PackageIdentity identity)
        {
            return new(identity, [], [], null);
        }
    }
}