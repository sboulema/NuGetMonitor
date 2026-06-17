using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Abstractions;
using NuGetMonitor.Model;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Model.Services;
using NuGetMonitor.Services;
using NuGetMonitor.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;

namespace NuGetMonitor.View.Monitor;

#pragma warning disable CA1812 // Avoid uninstantiated internal classes => used in xaml!

internal sealed partial class NuGetMonitorViewModel : INotifyPropertyChanged
{
    private static readonly string[] _versionMetadataNames = ["Version", "VersionOverride"];

    public NuGetMonitorViewModel()
    {
        PlatformAbstractions.SolutionOpened += SolutionEvents_OnAfterOpenSolution;
        PlatformAbstractions.SolutionClosed += SolutionEvents_OnAfterCloseSolution;

#pragma warning disable VSTHRD001
#pragma warning disable VSTHRD110
#pragma warning disable VSSDK008
        DispatcherExtensions.CurrentDispatcher.BeginInvoke(() => Load().FireAndForget());
#pragma warning restore VSTHRD110
#pragma warning restore VSTHRD001
#pragma warning restore VSSDK008
    }

    public ICollection<PackageViewModel>? Packages { get; private set; }

    public ObservableCollection<PackageViewModel> SelectedPackages { get; } = new();

    public bool IsLoading { get; set; }

    public ICommand UpdateSelectedCommand => new DelegateCommand(() => SelectedPackages.Any(item => item.IsUpdateAvailable), UpdateSelected);

    public ICommand NormalizePackageReferencesCommand => new DelegateCommand(NormalizePackageReferences);

    public ICommand CopyIssueDetailsCommand => new DelegateCommand(CanCopyIssueDetails, CopyIssueDetails);

    private void SolutionEvents_OnAfterOpenSolution(object? sender, EventArgs e)
    {
        Load().FireAndForget();
    }

    private void SolutionEvents_OnAfterCloseSolution(object? sender, EventArgs e)
    {
        Packages = null;
    }

    private async Task Load()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;

            Packages = null;

            var projectFolders = await PlatformAbstractions.GetProjectFilePaths();

            var packageReferences = await ProjectService.GetPackageReferences(projectFolders);

            var packages = packageReferences
                .GroupBy(item => item.Identity)
                .Select(group => new PackageViewModel(this, group, PackageItemType.PackageReference))
                .ToArray();

            var packageIds = packages
                .Select(item => item.PackageReference)
                .ToHashSet();

            var transitivePins = packages
                .SelectMany(item => item.Items)
                .Select(item => item.ProjectItemInTargetFramework)
                .Where(item => item.Project.IsTransitivePinningEnabled)
                .SelectMany(project => project.Project.CentralVersionMap.Values.Select(item => new PackageReferenceEntry(item.EvaluatedInclude, item.GetVersion() ?? VersionRange.None, VersionKind.CentralDefinition, item, project, false)))
                .Where(item => !packageIds.Contains(item.Identity))
                .GroupBy(item => item.Identity)
                .Select(group => new PackageViewModel(this, group, PackageItemType.PackageVersion))
                .ToArray();

            Packages = packages.Concat(transitivePins).ToArray();

            IsLoading = false;

            var loadVersionTasks = Packages.Select(item => item.LoadAsync());

            await Task.WhenAll(loadVersionTasks);

        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Loading package data failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Update(PackageViewModel packageViewModel)
    {
        UpdateAsync([packageViewModel]).FireAndForget();
    }

    private void UpdateSelected()
    {
        UpdateAsync(SelectedPackages.ToArray()).FireAndForget();
    }

    private async Task UpdateAsync(ICollection<PackageViewModel> packageViewModels)
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;

            using var projectCollection = new ProjectCollection();

            var viewModels = packageViewModels
                .Where(viewModel => viewModel.IsUpdateAvailable && viewModel.SelectedVersion is not null)
                .ToArray();

            if (!await AreTargetFrameworksCompatible(viewModels))
                return;

            var packageReferencesByProject = viewModels
                .SelectMany(viewModel => viewModel.Items.Select(item => new { item.Identity.Id, item.VersionSource, viewModel.ActiveVersion, item.VersionSource.GetContainingProject().FullPath, viewModel.SelectedVersion }))
                .GroupBy(item => item.FullPath);

            foreach (var packageReferenceEntries in packageReferencesByProject)
            {
                var fullPath = packageReferenceEntries.Key;

                var project = ProjectRootElement.Open(fullPath, projectCollection, true);

                var projectItems = project.Items;

                foreach (var packageReferenceEntry in packageReferenceEntries)
                {
                    var id = packageReferenceEntry.Id;
                    var selectedVersion = packageReferenceEntry.SelectedVersion;
                    var versionSource = packageReferenceEntry.VersionSource;
                    var currentVersion = packageReferenceEntry.ActiveVersion switch
                    {
                        NuGetVersion version => version.OriginalVersion,
                        VersionRange versionRange => versionRange.OriginalString,
                        _ => null
                    };

                    if (selectedVersion == null || currentVersion == null)
                        continue;

                    var metadataItems = projectItems
                        .Where(item => item.ItemType == versionSource.ItemType)
                        .Where(item => string.Equals(item.Include, id, StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Metadata.FirstOrDefault(metadata => _versionMetadataNames.Any(name => string.Equals(metadata.Name, name, StringComparison.OrdinalIgnoreCase))
                                                                                 && string.Equals(metadata.Value, currentVersion, StringComparison.OrdinalIgnoreCase)))
                        .ExceptNullItems();

                    foreach (var metadata in metadataItems)
                    {
                        metadata.Value = selectedVersion.ToString();
                    }
                }

                project.Save();
            }

            foreach (var packageViewModel in viewModels)
            {
                packageViewModel.ApplySelectedVersion();
            }

            ProjectService.ClearCache();
        }
        catch (OperationCanceledException)
        {
            // session cancelled
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Updating package failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task<bool> AreTargetFrameworksCompatible(PackageViewModel[] viewModels)
    {
        foreach (var viewModel in viewModels)
        {
            var selectedVersion = viewModel.SelectedVersion;
            var packageReference = viewModel.PackageReference;

            var packageDetails = await NuGetService.GetPackageDetails(new PackageIdentity(packageReference.Id, selectedVersion));
            if (packageDetails == null)
                continue;

            // e.g. Analyzer packages do not specify any target framework, they are just compatible with everything
            if (packageDetails.SupportedFrameworks.Count == 0)
                continue;

            var projects = viewModel.Items
                .Where(referenceEntry => referenceEntry.VersionKind is not VersionKind.CentralDefinition && !referenceEntry.VersionSource.IsGlobalPackageReference() && referenceEntry.Identity.Id == packageReference.Id && referenceEntry.Identity.VersionRange.Equals(packageReference.VersionRange))
                .Select(referenceEntry => referenceEntry.ProjectItemInTargetFramework.Project)
                .Distinct();

            var incompatibleProjects = projects
                .Where(project => !packageDetails.SupportedFrameworks.Any(supportedFramework => project.TargetFramework.IsCompatibleWith(supportedFramework)))
                .ToArray();

            if (incompatibleProjects.Length <= 0)
                continue;

            if (!await PlatformAbstractions.ShowNoYesMessageBox(
                    $"Package {packageDetails.Identity} ({string.Join(", ", packageDetails.SupportedFrameworks)}) is not compatible with projects {string.Join(",", incompatibleProjects.Select(p => p.NameAndFramework))}",
                    "Do you want to update anyway?"))
                return false;
        }

        return true;
    }

    private void NormalizePackageReferences()
    {
        NormalizePackageReferencesAsync().FireAndForget();
    }

    private async Task NormalizePackageReferencesAsync()
    {
        var projectItems = Packages?
            .SelectMany(p => p.Items.Select(item => item.ProjectItemInTargetFramework.ProjectItem)) ?? [];

        var numberOfUpdatedItems = ProjectService.NormalizePackageReferences(projectItems);

        await ShowInfoBar($"{numberOfUpdatedItems} package references normalized");
    }

    private static async Task ShowInfoBar(string text)
    {
        await PlatformAbstractions.ShowInfoBar(text);
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

        // Copy to clipboard
        ClipboardService.SetText(text.ToString());
    }
}