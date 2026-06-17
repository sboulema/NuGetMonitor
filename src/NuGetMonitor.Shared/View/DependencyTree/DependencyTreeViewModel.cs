using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGetMonitor.Abstractions;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Model.Services;
using NuGetMonitor.Services;
using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Throttle;

namespace NuGetMonitor.View.DependencyTree;

internal enum PackageNode
{
    PackageReference,
    PackageVersion,
    PackageMitigation
}

internal sealed partial class ChildNode : INotifyPropertyChanged
{
    private readonly PackageInfo _packageInfo;
    private readonly TransitiveDependencies _transitiveDependencies;
    private readonly HashSet<PackageInfo>? _dependsOn;

    public ChildNode(PackageInfo packageInfo, TransitiveDependencies transitiveDependencies)
    {
        _packageInfo = packageInfo;
        _transitiveDependencies = transitiveDependencies;

        transitiveDependencies.TransitivePackages.TryGetParents(packageInfo, out _dependsOn);
    }

    public PackageIdentity PackageIdentity => _packageInfo.PackageIdentity;

    public IEnumerable<ChildNode>? Children => _dependsOn?
        .OrderBy(item => item.PackageIdentity)
        .Select(item => new ChildNode(item, _transitiveDependencies));

    public bool HasChildren => _dependsOn != null;

    public string Issues => GetIssues();

    public bool IsOutdated => _packageInfo.IsOutdated;

    public bool IsVulnerable => _packageInfo.IsVulnerable;

    public bool IsTransitivePinned => _packageInfo.IsTransitivePinned;

    public ICommand CopyPackageReferenceCommand => new DelegateCommand(() => CopyNode(PackageNode.PackageReference));

    public ICommand CopyPackageVersionCommand => new DelegateCommand(() => CopyNode(PackageNode.PackageVersion));

    public ICommand CopyPackageMitigationCommand => new DelegateCommand(() => CopyNode(PackageNode.PackageMitigation));

    private void CopyNode(PackageNode node)
    {
        var currentVersion = PackageIdentity.Version;

        var version = node == PackageNode.PackageMitigation
            // mitigation is always for the current version
            ? currentVersion
            // for the other nodes we want the latest version of the same kind (pre-release or not)
            : _packageInfo.Package.Versions
                .Where(v => v.IsPrerelease == currentVersion.IsPrerelease)
                .DefaultIfEmpty(currentVersion)
                .Max();

        var justification = node == PackageNode.PackageMitigation ? "Justification=\"TODO\" " : string.Empty;

        ClipboardService.SetText($"""<{node} Include="{PackageIdentity.Id}" Version="{version}" {justification}/>""");

        if (node == PackageNode.PackageReference)
        {
            PlatformAbstractions.OpenDocument(_transitiveDependencies.ProjectFullPath);
        }
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
            yield return _packageInfo.VulnerabilityMitigation.IsNullOrEmpty() ? "Vulnerable" : $"Vulnerable ({_packageInfo.VulnerabilityMitigation})";
    }
}

internal sealed partial class RootNode : INotifyPropertyChanged
{
    private readonly TransitiveDependencies _transitiveDependencies;
    private readonly HashSet<ChildNode> _allChildren;
    private readonly ObservableCollection<ChildNode> _children;

    public RootNode(TransitiveDependencies transitiveDependencies)
    {
        _transitiveDependencies = transitiveDependencies;

        var children = _transitiveDependencies.TransitivePackages
            .OrderBy(item => item.PackageIdentity)
            .Select(item => new ChildNode(item, _transitiveDependencies))
            .ToHashSet();

        _allChildren = children;

        _children = new ObservableCollection<ChildNode>(children);
    }

    public string ProjectName => _transitiveDependencies.ProjectName;

    public NuGetFramework TargetFramework => _transitiveDependencies.TargetFramework;

    public ObservableCollection<ChildNode> Children => _children;

    public void SetFilter(string? searchText, bool showUpToDate, bool showOutdated, bool showVulnerable)
    {
        if (searchText.IsNullOrWhiteSpace() && showUpToDate && showOutdated && showVulnerable)
        {
            SetChildren(_allChildren);
            return;
        }

        bool Filter(ChildNode item)
        {
            var packageIdentity = item.PackageIdentity;
            var isOutdated = item.IsOutdated;
            var isVulnerable = item.IsVulnerable;

            return (searchText.IsNullOrWhiteSpace() || packageIdentity.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase))
                   && (showUpToDate || isOutdated || isVulnerable)
                   && (showOutdated || !isOutdated || isVulnerable)
                   && (showVulnerable || !isVulnerable);
        }

        SetChildren(_allChildren.Where(Filter).ToHashSet());
    }

    private void SetChildren(HashSet<ChildNode> children)
    {
        var index = 0;

        _children.RemoveWhere(child => !children.Contains(child));

        foreach (var child in children)
        {
            if (index >= _children.Count || _children[index] != child)
            {
                _children.Insert(index, child);
            }

            index += 1;
        }
    }
}

#pragma warning disable CA1812 // Avoid uninstantiated internal classes => used in xaml!
internal sealed partial class DependencyTreeViewModel : INotifyPropertyChanged
{
    public DependencyTreeViewModel()
    {
        PlatformAbstractions.SolutionOpened += SolutionEvents_OnAfterOpenSolution;
        PlatformAbstractions.SolutionClosed += SolutionEvents_OnAfterCloseSolution;

#pragma warning disable VSTHRD001
#pragma warning disable VSTHRD110
#pragma warning disable VSSDK008
        DispatcherExtensions.CurrentDispatcher.BeginInvoke(() => Load().FireAndForget());
#pragma warning restore VSTHRD110
#pragma warning restore VSTHRD001
#pragma warning restore VSTHRD008
    }

    public bool IsLoading { get; set; }

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
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;

            var projectFilePaths = await PlatformAbstractions.GetProjectFilePaths();

            var packageReferences = await ProjectService.GetPackageReferences(projectFilePaths).ConfigureAwait(true);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences).ConfigureAwait(true);

            if (topLevelPackages.Count == 0)
                return;

            var transitivePackages = await NuGetService.GetTransitiveDependencies(topLevelPackages).ConfigureAwait(true);

            TransitivePackages = transitivePackages
                .OrderBy(item => item.ProjectName)
                .ThenBy(item => item.TargetFramework.ToString())
                .Select(item => new RootNode(item))
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