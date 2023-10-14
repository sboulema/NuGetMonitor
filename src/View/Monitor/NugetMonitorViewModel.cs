using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Community.VisualStudio.Toolkit;
using DataGridExtensions;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View;

#pragma warning disable CA1812 // Avoid uninstantiated internal classes => used in xaml!
internal sealed partial class NuGetMonitorViewModel : INotifyPropertyChanged
{
    public NuGetMonitorViewModel()
    {
        VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        VS.Events.SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
        Load();
    }

    public ICollection<PackageViewModel>? Packages { get; private set; }

    public ObservableCollection<PackageViewModel> SelectedPackages { get; } = new();

    public bool IsLoading { get; set; } = true;

    public ICommand UpdateSelectedCommand => new DelegateCommand(() => SelectedPackages.Any(item => item.IsUpdateAvailable), UpdateSelected);

    public ICommand RefreshCommand => new DelegateCommand<DataGrid>(Refresh);
    
    public static ICommand ShowDependencyTreeCommand => new DelegateCommand(ShowDependencyTree);

    public static ICommand ShowNuGetPackageManagerCommand => new DelegateCommand(ShowNuGetPackageManager);

    public ICommand CopyIssueDetailsCommand => new DelegateCommand(CanCopyIssueDetails, CopyIssueDetails);

    private void SolutionEvents_OnAfterOpenSolution(Solution? obj)
    {
        Load();
    }

    private void SolutionEvents_OnAfterCloseSolution()
    {
        Packages = null;
    }

    private async void Load()
    {
        try
        {
            IsLoading = true;

            Packages = null;

            var packageReferences = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var packages = packageReferences
                .GroupBy(item => item.Identity)
                .Select(group => new PackageViewModel(group))
                .ToArray();

            Packages = packages;

            IsLoading = false;

            var loadVersionTasks = packages.Select(item => item.Load());

            await Task.WhenAll(loadVersionTasks);

        }
        catch (Exception ex)
        {
            await LogAsync($"Loading package data failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static void ShowNuGetPackageManager()
    {
        VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
    }

    private static void ShowDependencyTree()
    {
        NuGetMonitorCommands.Instance?.ShowDependencyTreeToolWindow();
    }

    private void Refresh(DataGrid dataGrid)
    {
        dataGrid.GetFilter().Clear();

        MonitorService.CheckForUpdates();

        Load();
    }

    public static void Update(PackageViewModel packageViewModel)
    {
        Update(new[] { packageViewModel });
    }

    private void UpdateSelected()
    {
        Update(SelectedPackages.ToArray());
    }

    private static void Update(ICollection<PackageViewModel> packageViewModels)
    {
        using var projectCollection = new ProjectCollection();

        var packageReferencesByProject = packageViewModels
            .Where(viewModel => viewModel.IsUpdateAvailable)
            .SelectMany(viewModel => viewModel.Items.Select(item => new { item.Identity, item.ProjectItemInTargetFramework.ContainingProject.FullPath, viewModel.SelectedVersion }))
            .GroupBy(item => item.FullPath);

        foreach (var packageReferenceEntries in packageReferencesByProject)
        {
            var fullPath = packageReferenceEntries.Key;

            var project = ProjectRootElement.Open(fullPath, projectCollection, true);

            var packageReferences = project.Items;

            foreach (var packageReferenceEntry in packageReferenceEntries)
            {
                var identity = packageReferenceEntry.Identity;
                var selectedVersion = packageReferenceEntry.SelectedVersion;

                if (selectedVersion == null)
                    continue;

                var metadataItems = packageReferences
                    .Where(item => string.Equals(item.Include, identity.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Metadata.FirstOrDefault(metadata => string.Equals(metadata.Name, "Version", StringComparison.OrdinalIgnoreCase)
                                                                          && string.Equals(metadata.Value, identity.VersionRange.OriginalString, StringComparison.OrdinalIgnoreCase)))
                    .ExceptNullItems();

                foreach (var metadata in metadataItems)
                {
                    metadata.Value = selectedVersion.ToString();
                    metadata.ExpressedAsAttribute = true;
                }
            }

            project.Save();
        }

        foreach (var packageViewModel in packageViewModels)
        {
            packageViewModel.ApplySelectedVersion();
        }

        ProjectService.ClearCache();
    }

    private bool CanCopyIssueDetails()
    {
        return Packages?.Any(p => p.PackageInfo?.HasIssues ?? false) == true;
    }

    private void CopyIssueDetails()
    {
        if (Packages is null)
            return;

        var text = new StringBuilder();

        foreach (var package in Packages)
        {
            package.PackageInfo?.AppendIssueDetails(text);
        }

        Clipboard.SetText(text.ToString());
    }
}