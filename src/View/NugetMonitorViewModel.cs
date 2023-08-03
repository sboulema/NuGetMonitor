using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Models;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View;

internal partial class PackageViewModel : INotifyPropertyChanged
{
    private readonly NugetMonitorViewModel _owner;

    public PackageViewModel(IGrouping<PackageIdentity, PackageReferenceEntry> items, NugetMonitorViewModel owner)
    {
        _owner = owner;
        Items = items;
        Identity = items.Key;
        ProjectPaths = string.Join(", ", items.Select(item => item.RelativePath));
    }

    public IGrouping<PackageIdentity, PackageReferenceEntry> Items { get; }

    public PackageIdentity Identity { get; private set; }

    public string ProjectPaths { get; }

    public async Task Load()
    {
        Package = await NuGetService.GetPackage(Identity.Id).ConfigureAwait(false);
        SelectedVersion = Package.Versions.FirstOrDefault(i => !i.IsPrerelease && i >= Identity.Version) ?? Package.Versions.FirstOrDefault();
        IsSelected = IsUpdateAvailable;
    }

    public Package? Package { get; private set; }

    public NuGetVersion? SelectedVersion { get; set; }

    public bool IsUpdateAvailable => SelectedVersion > Identity.Version;

    public bool IsSelected { get; set; }

    public ICommand Update => new DelegateCommand(() => { _owner.Update(this); });

    public void ApplyVersion()
    {
        Identity = new PackageIdentity(Identity.Id, SelectedVersion);
    }
}

internal partial class NugetMonitorViewModel : INotifyPropertyChanged
{
    public static readonly NugetMonitorViewModel Instance = new();

    private NugetMonitorViewModel()
    {
        VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        Load();
    }

    private void SolutionEvents_OnAfterOpenSolution(Solution? obj)
    {
        Load();
    }

    private async void Load()
    {
        try
        {
            IsLoading = true;

            Packages.Clear();

            var packageReferences = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var items = packageReferences
                .GroupBy(item => item.Identity)
                .Select(group => new PackageViewModel(group, this))
                .ToArray();

            Packages.AddRange(items);

            var loadVersionTasks = Packages.Select(item => item.Load());

            await Task.WhenAll(loadVersionTasks);

            IsLoading = false;
        }
        catch
        {
            // 
        }
    }

    public ObservableCollection<PackageViewModel> Packages { get; } = new();

    public ObservableCollection<PackageViewModel> SelectedPackages { get; } = new();

    public bool IsLoading { get; set; } = true;

    public ICommand UpdateSelectedCommand => new DelegateCommand(UpdateSelected);

    public ICommand SoftRefreshCommand => new DelegateCommand(SoftRefresh);

    public ICommand HardRefreshCommand => new DelegateCommand(HardRefresh);

    private void HardRefresh()
    {
        NuGetService.ClearCache();
        Load();
    }
    
    private void SoftRefresh()
    {
        Load();     
    }

    public void Update(PackageViewModel packageViewModel)
    {
        Update(new[] { packageViewModel });
    }

    private void UpdateSelected()
    {
        Update(SelectedPackages.ToArray());
    }

    private void Update(ICollection<PackageViewModel> packageViewModels)
    {
        using var projectCollection = new ProjectCollection();

        var packageReferencesByProject = packageViewModels
            .SelectMany(viewModel => viewModel.Items.Select(item => new { item.Identity, item.ProjectItem.Xml.ContainingProject.FullPath, viewModel.SelectedVersion }))
            .GroupBy(item => item.FullPath);

        foreach (var packageReferenceEntries in packageReferencesByProject)
        {
            var fullPath = packageReferenceEntries.Key;

            var project = ProjectRootElement.Open(fullPath, projectCollection, true);

            var packageReferences = project.Items
                .Where(IsEditablePackageReference)
                .ToArray();

            foreach (var packageReferenceEntry in packageReferenceEntries)
            {
                var identity = packageReferenceEntry.Identity;
                var selectedVersion = packageReferenceEntry.SelectedVersion;

                if (selectedVersion == null)
                    continue;

                var metadata = packageReferences
                    .Where(item => string.Equals(item.Include, identity.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Metadata.FirstOrDefault(metadata => string.Equals(metadata.Name, "Version", StringComparison.OrdinalIgnoreCase)
                                                                          && string.Equals(metadata.Value, identity.Version.ToString(), StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault();

                if (metadata == null)
                    continue;

                metadata.Value = selectedVersion.ToString();
                metadata.ExpressedAsAttribute = true;
            }

            project.Save();
        }

        foreach (var packageViewModel in packageViewModels)
        {
            packageViewModel.ApplyVersion();
        }
    }

    private static bool IsEditablePackageReference(ProjectItemElement element)
    {
        return ProjectService.IsEditablePackageReference(element.ItemType, element.Metadata.Select(value => new KeyValuePair<string, string?>(value.Name, value.Value)));
    }
}