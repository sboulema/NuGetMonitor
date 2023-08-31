using Microsoft.Build.Evaluation;
using NuGet.Frameworks;

namespace NuGetMonitor.Models;

internal sealed record TransitiveDependencies(Project Project, NuGetFramework TargetFramework, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> ParentsByChild);