using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace NuGetMonitor.Model.Models
{
    public sealed record PackageDetails(PackageIdentity Identity, IReadOnlyCollection<PackageDependencyGroup> DependencyGroups, IReadOnlyCollection<NuGetFramework> SupportedFrameworks);
}