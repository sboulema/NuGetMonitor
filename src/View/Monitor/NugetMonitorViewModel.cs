﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DataGridExtensions;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Model.Abstractions;
using NuGetMonitor.Model.Services;
using NuGetMonitor.Services;
using TomsToolbox.Essentials;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View;

#pragma warning disable CA1812 // Avoid uninstantiated internal classes => used in xaml!
internal sealed partial class NuGetMonitorViewModel : INotifyPropertyChanged
{
    private static readonly string[] _versionMetadataNames = { "Version", "VersionOverride" };

    private readonly ISolutionService _solutionService;

    public NuGetMonitorViewModel(ISolutionService solutionService)
    {
        _solutionService = solutionService;

        solutionService.SolutionOpened += SolutionEvents_OnAfterOpenSolution;
        solutionService.SolutionClosed += SolutionEvents_OnAfterCloseSolution;

        Load();
    }

    public ICollection<PackageViewModel>? Packages { get; private set; }

    public ObservableCollection<PackageViewModel> SelectedPackages { get; } = new();

    public bool IsLoading { get; set; } = true;

    public ICommand UpdateSelectedCommand => new DelegateCommand(() => SelectedPackages.Any(item => item.IsUpdateAvailable), UpdateSelected);

    public ICommand RefreshCommand => new DelegateCommand<DataGrid>(Refresh);

    public static ICommand ShowDependencyTreeCommand => new DelegateCommand(ShowDependencyTree);

    public ICommand ShowNuGetPackageManagerCommand => new DelegateCommand(() => _solutionService.ShowPackageManager());

    public ICommand CopyIssueDetailsCommand => new DelegateCommand(CanCopyIssueDetails, CopyIssueDetails);

    public ICommand NormalizePackageReferencesCommand => new DelegateCommand(NormalizePackageReferences);

    private void SolutionEvents_OnAfterOpenSolution(object? sender, EventArgs e)
    {
        Load();
    }

    private void SolutionEvents_OnAfterCloseSolution(object? sender, EventArgs e)
    {
        Packages = null;
    }

    private async void Load()
    {
        try
        {
            IsLoading = true;

            Packages = null;

            var projectFolders = await _solutionService.GetProjectFolders();

            var packageReferences = await ProjectService.GetPackageReferences(projectFolders);

            var packages = packageReferences
                .GroupBy(item => item.Identity)
                .Select(group => new PackageViewModel(group, _solutionService))
                .ToArray();

            Packages = packages;

            IsLoading = false;

            var loadVersionTasks = packages.Select(item => item.Load());

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
            .SelectMany(viewModel => viewModel.Items.Select(item => new { item.Identity, item.VersionSource, item.VersionSource.GetContainingProject().FullPath, viewModel.SelectedVersion }))
            .GroupBy(item => item.FullPath);

        foreach (var packageReferenceEntries in packageReferencesByProject)
        {
            var fullPath = packageReferenceEntries.Key;

            var project = ProjectRootElement.Open(fullPath, projectCollection, true);

            var projectItems = project.Items;

            foreach (var packageReferenceEntry in packageReferenceEntries)
            {
                var identity = packageReferenceEntry.Identity;
                var selectedVersion = packageReferenceEntry.SelectedVersion;
                var versionSource = packageReferenceEntry.VersionSource;

                if (selectedVersion == null)
                    continue;

                var metadataItems = projectItems
                    .Where(item => item.ItemType == versionSource.ItemType)
                    .Where(item => string.Equals(item.Include, identity.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Metadata.FirstOrDefault(metadata => _versionMetadataNames.Any(name => string.Equals(metadata.Name, name, StringComparison.OrdinalIgnoreCase))
                                                                          && string.Equals(metadata.Value, identity.VersionRange.OriginalString, StringComparison.OrdinalIgnoreCase)))
                    .ExceptNullItems();

                foreach (var metadata in metadataItems)
                {
                    metadata.Value = selectedVersion.ToString();
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

    private void NormalizePackageReferences()
    {
        NormalizePackageReferencesAsync().FireAndForget();
    }

    private async Task NormalizePackageReferencesAsync()
    {
        var projectItems = Packages
            .SelectMany(p => p.Items.Select(item => item.ProjectItemInTargetFramework.ProjectItem));

        var numberOfUpdatedItems = ProjectService.NormalizePackageReferences(projectItems);

        await ShowInfoBar($"{numberOfUpdatedItems} package references normalized");
    }

    private async Task ShowInfoBar(string text)
    {
        await _solutionService.ShowInfoBar(text);
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