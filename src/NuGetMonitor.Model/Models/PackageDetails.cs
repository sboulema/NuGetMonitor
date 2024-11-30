using System.Collections.ObjectModel;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace NuGetMonitor.Model.Models
{
    public sealed record PackageDetails(IReadOnlyCollection<PackageDependencyGroup> DependencyGroups, IReadOnlyCollection<NuGetFramework> SupportedFrameworks);
}