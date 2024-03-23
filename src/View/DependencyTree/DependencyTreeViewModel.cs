using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using NuGet.Frameworks;
using TomsToolbox.Wpf;
using NuGet.Packaging.Core;
using NuGetMonitor.Abstractions;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Model.Services;
using PropertyChanged;
using Throttle;
using TomsToolbox.Essentials;

namespace NuGetMonitor.View.DependencyTree;

internal sealed partial class ChildNode : INotifyPropertyChanged
{
    private readonly PackageInfo _packageInfo;
    private readonly TransitiveDependencies _transitiveDependencies;
    private readonly ISolutionService _solutionService;
    private readonly HashSet<PackageInfo>? _dependsOn;

    public ChildNode(PackageInfo packageInfo, TransitiveDependencies transitiveDependencies, ISolutionService solutionService)
    {
        _packageInfo = packageInfo;
        _transitiveDependencies = transitiveDependencies;
        _solutionService = solutionService;

        transitiveDependencies.ParentsByChild.TryGetValue(packageInfo, out _dependsOn);
    }

    public PackageIdentity PackageIdentity => _packageInfo.PackageIdentity;

    public IEnumerable<ChildNode>? Children => _dependsOn?
        .OrderBy(item => item.PackageIdentity)
        .Select(item => new ChildNode(item, _transitiveDependencies, _solutionService));

    public bool HasChildren => _dependsOn != null;

    public string Issues => GetIssues();

    public bool IsOutdated => _packageInfo.IsOutdated;

    public bool IsVulnerable => _packageInfo.IsVulnerable;

    public ICommand CopyPackageReferenceCommand => new DelegateCommand(CopyPackageReference);

    private void CopyPackageReference()
    {
        Clipboard.SetText($"""<PackageReference Include="{PackageIdentity.Id}" Version="{PackageIdentity.Version}" />""");
        _solutionService.OpenDocument(_transitiveDependencies.ProjectFullPath);
    }

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

    public RootNode(TransitiveDependencies transitiveDependencies, ISolutionService solutionService)
    {
        _transitiveDependencies = transitiveDependencies;

        var children = _transitiveDependencies.ParentsByChild
            .OrderBy(item => item.Key.PackageIdentity)
            .Select(item => new ChildNode(item.Key, _transitiveDependencies, solutionService))
            .ToArray();

        _children = new ListCollectionView(children);
    }

    public string ProjectName => _transitiveDependencies.ProjectName;

    public NuGetFramework TargetFramework => _transitiveDependencies.TargetFramework;

    public ICollectionView Children => _children;

    public void SetFilter(string? searchText, bool showUpToDate, bool showOutdated, bool showVulnerable)
    {
        if (searchText.IsNullOrWhiteSpace() && showUpToDate && showOutdated && showVulnerable)
        {
            _children.Filter = null;
            return;
        }

        _children.Filter = item =>
        {
            var childNode = (ChildNode)item;
            var packageIdentity = childNode.PackageIdentity;
            var isOutdated = childNode.IsOutdated;
            var isVulnerable = childNode.IsVulnerable;

            return (searchText.IsNullOrWhiteSpace() || packageIdentity.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                && (showUpToDate || isOutdated || isVulnerable)
                && (showOutdated || !isOutdated || isVulnerable)
                && (showVulnerable || !isVulnerable);
        };
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

    [OnChangedMethod(nameof(OnFilterChanged))]
    public bool ShowUpToDate { get; set; } = true;

    [OnChangedMethod(nameof(OnFilterChanged))]
    public bool ShowOutdated { get; set; } = true;

    [OnChangedMethod(nameof(OnFilterChanged))]
    public bool ShowVulnerable { get; set; } = true;

    public ICollection<RootNode>? TransitivePackages { get; private set; }

    public ICommand RefreshCommand => new DelegateCommand(Refresh);

    [OnChangedMethod(nameof(OnSearchTextChanged))]
    public string? SearchText { get; set; }

    [Throttled(typeof(TomsToolbox.Wpf.Throttle), 200)]
    private void OnSearchTextChanged()
    {
        TransitivePackages?.ForEach(item => item.SetFilter(SearchText, ShowUpToDate, ShowOutdated, ShowVulnerable));
    }

    private void OnFilterChanged()
    {
        TransitivePackages?.ForEach(item => item.SetFilter(SearchText, ShowUpToDate, ShowOutdated, ShowVulnerable));
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

            var projectFilePaths = await _solutionService.GetProjectFilePaths();

            var packageReferences = await ProjectService.GetPackageReferences(projectFilePaths).ConfigureAwait(true);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences).ConfigureAwait(true);

            if (topLevelPackages.Count == 0)
                return;

            var transitivePackages = await NuGetService.GetTransitivePackages(topLevelPackages).ConfigureAwait(true);

            TransitivePackages = transitivePackages
                .OrderBy(item => item.ProjectName)
                .ThenBy(item => item.TargetFramework.ToString())
                .Select(item => new RootNode(item, _solutionService))
                .ToArray();

            OnSearchTextChanged();
            OnFilterChanged();
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