using Microsoft.Build.Evaluation;
using NuGet.Frameworks;

namespace NuGetMonitor.Models;

internal record TransitiveDependencies(Project Project, NuGetFramework TargetFramework, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> Packages);