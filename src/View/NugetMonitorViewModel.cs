using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View;

internal partial class NugetMonitorViewModel : INotifyPropertyChanged
{
    public NugetMonitorViewModel()
    {
        VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        VS.Events.SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
        Load();
    }

    public ICollection<PackageViewModel>? Packages { get; private set; }

    public ObservableCollection<PackageViewModel> SelectedPackages { get; } = new();

    public bool IsLoading { get; set; } = true;

    public ICommand UpdateSelectedCommand => new DelegateCommand(() => SelectedPackages.Any(item => item.IsUpdateAvailable), UpdateSelected);

    public ICommand RefreshCommand => new DelegateCommand(Refresh);
    
    public static ICommand ShowNuGetPackageManagerCommand => new DelegateCommand(ShowNuGetPackageManager);

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

            var loadVersionTasks = packages.Select(item => item.Load());

            await Task.WhenAll(loadVersionTasks).ConfigureAwait(false);

            IsLoading = false;
        }
        catch (Exception ex)
        {
            LoggingService.Log($"Loading package data failed: {ex}").FireAndForget();
        }
    }

    private static void ShowNuGetPackageManager()
    {
        VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
    }

    private void Refresh()
    {
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