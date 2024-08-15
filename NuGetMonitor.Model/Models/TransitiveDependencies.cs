using NuGet.Frameworks;

namespace NuGetMonitor.Model.Models;

public sealed record TransitiveDependencies(ProjectInTargetFramework Project, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> ParentsByChild)
{
    public string ProjectName => Path.GetFileName(ProjectFullPath);

    public string ProjectFullPath => Project.Project.FullPath;

    public NuGetFramework TargetFramework => Project.TargetFramework;
}