using NuGetMonitor.Services;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using NuGet.Frameworks;
using NuGetMonitor.Models;
using TomsToolbox.Wpf;
using NuGet.Packaging.Core;
using NuGetMonitor.Model.Abstractions;
using PropertyChanged;
using Throttle;
using TomsToolbox.Essentials;

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

        parentsByChild.TryGetValue(packageInfo, out _dependsOn);
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
    private readonly ListCollectionView _children;

    public RootNode(TransitiveDependencies transitiveDependencies)
    {
        _transitiveDependencies = transitiveDependencies;

        var children = _transitiveDependencies.ParentsByChild
            .OrderBy(item => item.Key.PackageIdentity)
            .Select(item => new ChildNode(item.Key, _transitiveDependencies.ParentsByChild))
            .ToArray();

        _children = new ListCollectionView(children);
    }

    public string ProjectName => _transitiveDependencies.ProjectName;

    public NuGetFramework TargetFramework => _transitiveDependencies.TargetFramework;

    public ICollectionView Children => _children;

    public void SetFilter(string? searchText)
    {
        if (searchText.IsNullOrWhiteSpace())
        {
            _children.Filter = null;
            return;
        }

        _children.Filter = item => ((ChildNode)item).PackageIdentity.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

#pragma warning disable CA1812 // Avoid uninstantiated internal classes => used in xaml!
internal sealed partial class DependencyTreeViewModel : INotifyPropertyChanged
{
    private readonly ISolutionService _solutionService;

    public DependencyTreeViewModel(ISolutionService solutionService)
    {
        _solutionService = solutionService;

        solutionService.SolutionOpened += SolutionEvents_OnAfterOpenSolution;
        solutionService.SolutionClosed += SolutionEvents_OnAfterCloseSolution;

        Load().FireAndForget();
    }

    public bool IsLoading { get; set; } = true;

    public ICollection<RootNode>? TransitivePackages { get; private set; }

    public ICommand RefreshCommand => new DelegateCommand(Refresh);

    [OnChangedMethod(nameof(OnSearchTextChanged))]
    public string? SearchText { get; set; }

    [Throttled(typeof(TomsToolbox.Wpf.Throttle), 200)]
    private void OnSearchTextChanged()
    {
        TransitivePackages?.ForEach(item => item.SetFilter(SearchText));
    }

    private void Refresh()
    {
        ProjectService.ClearCache();

        Load().FireAndForget();
    }

    private async Task Load()
    {
        try
        {
            IsLoading = true;

            var projectFolders = await _solutionService.GetProjectFolders();

            var packageReferences = await ProjectService.GetPackageReferences(projectFolders).ConfigureAwait(true);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences).ConfigureAwait(true);

            if (topLevelPackages.Count == 0)
                return;

            var transitivePackages = await NuGetService.GetTransitivePackages(packageReferences, topLevelPackages).ConfigureAwait(true);

            TransitivePackages = transitivePackages
                .OrderBy(item => item.ProjectName)
                .ThenBy(item => item.TargetFramework.ToString())
                .Select(item => new RootNode(item))
                .ToArray();

            OnSearchTextChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SolutionEvents_OnAfterOpenSolution(object? sender, EventArgs e)
    {
        Load().FireAndForget();
    }

    private void SolutionEvents_OnAfterCloseSolution(object? sender, EventArgs e)
    {
        TransitivePackages = null;
    }
}