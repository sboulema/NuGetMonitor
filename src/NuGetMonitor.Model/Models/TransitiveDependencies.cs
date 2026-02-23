using System.Collections;
using NuGet.Frameworks;

namespace NuGetMonitor.Model.Models;

public sealed class TransitivePackages(IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> packagesToParentsMap) : IEnumerable<PackageInfo>
{
    public bool TryGetParents(PackageInfo packageInfo, out HashSet<PackageInfo>? parents)
    {
        return packagesToParentsMap.TryGetValue(packageInfo, out parents);
    }

    public IEnumerable<PackageInfo> Items => packagesToParentsMap.Keys;

    public IEnumerator<PackageInfo> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record TransitiveDependencies(ProjectInTargetFramework Project, ICollection<PackageInfo> TopLevelPackages, IReadOnlyDictionary<string, PackageInfo> InheritedDependencies, TransitivePackages TransitivePackages)
{
    public string ProjectName => Path.GetFileName(ProjectFullPath);

    public string ProjectFullPath => Project.Project.FullPath;

    public NuGetFramework TargetFramework => Project.TargetFramework;

    public IEnumerable<PackageInfo> AllDependencies => TopLevelPackages.Concat(TransitivePackages).Concat(InheritedDependencies.Values);
}