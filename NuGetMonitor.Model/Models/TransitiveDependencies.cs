using NuGet.Frameworks;

namespace NuGetMonitor.Models;

public sealed record TransitiveDependencies(string ProjectName, string ProjectFullPath, NuGetFramework TargetFramework, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> ParentsByChild);