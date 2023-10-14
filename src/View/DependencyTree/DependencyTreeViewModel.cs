using Community.VisualStudio.Toolkit;
using NuGetMonitor.Services;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using NuGet.Frameworks;
using NuGetMonitor.Models;
using TomsToolbox.Wpf;
using NuGet.Packaging.Core;

namespace NuGetMonitor.View.DependencyTree;

internal sealed partial class ChildNode : INotifyPropertyChanged
{
    private readonly PackageInfo _packageInfo;
    private readonly IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> _parentsByChild;
    private readonly HashSet<PackageInfo>? _dependsOn;

    public ChildNode(PackageInfo packageInfo, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> parentsByChild)
    {
        _packageInfo = packageInfo;
        _parentsByChild = parentsByChild;

        parentsByChild.TryGetValue(_packageInfo, out _dependsOn);
    }

    public PackageIdentity PackageIdentity => _packageInfo.PackageIdentity;

    public IEnumerable<ChildNode>? Children => _dependsOn?
        .OrderBy(item => item.PackageIdentity)
        .Select(item => new ChildNode(item, _parentsByChild));

    public bool HasChildren => _dependsOn != null;

    public string Issues => GetIssues();

    private string GetIssues()
    {
        var items = GetIssueItems().ToArray();
        if (items.Length == 0)
            return string.Empty;

        return $" [{string.Join(", ", items)}]";
    }

    private IEnumerable<string> GetIssueItems()
    {
        if (_packageInfo.IsDeprecated)
            yield return "Deprecated";

        if (_packageInfo.IsOutdated)
            yield return "Outdated";

        if (_packageInfo.IsVulnerable)
            yield return "Vulnerable";
    }
}

internal sealed partial class RootNode : INotifyPropertyChanged
{
    private readonly TransitiveDependencies _transitiveDependencies;

    public RootNode(TransitiveDependencies transitiveDependencies)
    {
        _transitiveDependencies = transitiveDependencies;
    }

    public string ProjectName => _transitiveDependencies.ProjectName;

    public NuGetFramework TargetFramework => _transitiveDependencies.TargetFramework;

    public IEnumerable<ChildNode> Children => _transitiveDependencies.ParentsByChild
        .OrderBy(item => item.Key.PackageIdentity)
        .Select(item => new ChildNode(item.Key, _transitiveDependencies.ParentsByChild));
}

#pragma warning disable CA1812 // Avoid uninstantiated internal classes => used in xaml!
internal sealed partial class DependencyTreeViewModel : INotifyPropertyChanged
{
    public DependencyTreeViewModel()
    {
        VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        VS.Events.SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
        Load().FireAndForget();
    }

    public bool IsLoading { get; set; } = true;

    public ICollection<RootNode>? TransitivePackages { get; private set; }

    public ICommand RefreshCommand => new DelegateCommand(() => Load().FireAndForget());

    public async Task Load()
    {
        try
        {
            IsLoading = true;

            var packageReferences = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences).ConfigureAwait(true);

            if (topLevelPackages.Count == 0)
                return;

            var transitivePackages = await NuGetService.GetTransitivePackages(packageReferences, topLevelPackages).ConfigureAwait(true);

            TransitivePackages = transitivePackages
                .OrderBy(item => item.ProjectName)
                .ThenBy(item => item.TargetFramework.ToString())
                .Select(item => new RootNode(item)).ToArray();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SolutionEvents_OnAfterOpenSolution(Solution? obj)
    {
        Load().FireAndForget();
    }

    private void SolutionEvents_OnAfterCloseSolution()
    {
        TransitivePackages = null;
    }
}