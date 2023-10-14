using NuGet.Frameworks;

namespace NuGetMonitor.Models;

internal sealed record TransitiveDependencies(string ProjectName, NuGetFramework TargetFramework, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> ParentsByChild);