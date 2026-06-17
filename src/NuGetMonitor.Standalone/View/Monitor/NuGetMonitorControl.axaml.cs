using Avalonia.Interactivity;
using NuGetMonitor.ViewModels;
using PropertyChanged;
using System.Collections.Specialized;

namespace NuGetMonitor.View.Monitor;

[DoNotNotify]
public sealed partial class NuGetMonitorControl : UserControl
{
    private bool _isUpdatingSelection;

    public NuGetMonitorControl()
    {
        InitializeComponent();
        DataGrid.SelectionChanged += DataGrid_SelectionChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Subscribe to ViewModel's SelectedPackages collection changes
        if (DataContext is NuGetMonitor.View.Monitor.NuGetMonitorViewModel viewModel)
        {
            viewModel.SelectedPackages.CollectionChanged += SelectedPackages_CollectionChanged;
            SyncViewModelToDataGrid();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        // Unsubscribe from events
        if (DataContext is NuGetMonitor.View.Monitor.NuGetMonitorViewModel viewModel)
        {
            viewModel.SelectedPackages.CollectionChanged -= SelectedPackages_CollectionChanged;
        }
    }

    private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection || DataContext is not NuGetMonitor.View.Monitor.NuGetMonitorViewModel viewModel)
            return;

        _isUpdatingSelection = true;
        try
        {
            // Remove deselected items
            foreach (var item in e.RemovedItems)
            {
                if (item is PackageViewModel package)
                {
                    viewModel.SelectedPackages.Remove(package);
                }
            }

            // Add newly selected items
            foreach (var item in e.AddedItems)
            {
                if (item is PackageViewModel package && !viewModel.SelectedPackages.Contains(package))
                {
                    viewModel.SelectedPackages.Add(package);
                }
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void SelectedPackages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdatingSelection)
            return;

        _isUpdatingSelection = true;
        try
        {
            SyncViewModelToDataGrid();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void SyncViewModelToDataGrid()
    {
        if (DataContext is not NuGetMonitor.View.Monitor.NuGetMonitorViewModel viewModel)
            return;

        DataGrid.SelectedItems.Clear();

        foreach (var package in viewModel.SelectedPackages)
        {
            DataGrid.SelectedItems.Add(package);
        }
    }
}
