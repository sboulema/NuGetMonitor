using Avalonia.Interactivity;
using NuGetMonitor.ViewModels;
using PropertyChanged;
using System.Collections.ObjectModel;
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

        InitializeColumnVisibility();
    }

    /// <summary>
    /// Backs the "Choose columns" flyout; one entry per toggleable column of <see cref="DataGrid"/>.
    /// </summary>
    public ObservableCollection<ColumnItem> ColumnItems { get; } = new();

    private void InitializeColumnVisibility()
    {
        var hiddenColumns = new HashSet<string>(
            Settings.Instance.HiddenColumns.Split(',').Where(header => !string.IsNullOrEmpty(header)),
            StringComparer.OrdinalIgnoreCase);

        // Package is the primary identity column; it should always stay visible and isn't offered in the chooser.
        var toggleableColumns = DataGrid.Columns.Where(column => !string.Equals(column.Header?.ToString(), "Package", StringComparison.Ordinal));

        foreach (var column in toggleableColumns)
        {
            var header = column.Header?.ToString();

            if (header is not null && hiddenColumns.Contains(header))
            {
                column.IsVisible = false;
            }

            ColumnItems.Add(new ColumnItem(column, PersistColumnVisibility));
        }
    }

    private void PersistColumnVisibility()
    {
        var hiddenColumns = DataGrid.Columns
            .Where(column => !column.IsVisible)
            .Select(column => column.Header?.ToString())
            .ExceptNullItems();

        Settings.Instance.HiddenColumns = string.Join(",", hiddenColumns);
        Settings.Instance.Save();
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
