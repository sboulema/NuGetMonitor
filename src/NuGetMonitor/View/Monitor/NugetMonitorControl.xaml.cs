using System.Collections.ObjectModel;
using System.Windows;
using NuGetMonitor.Options;

namespace NuGetMonitor.View.Monitor;

/// <summary>
/// Interaction logic for NugetMonitorControl.xaml
/// </summary>
public sealed partial class NuGetMonitorControl
{
    public NuGetMonitorControl()
    {
        InitializeComponent();

        DataContext = new NuGetMonitorViewModel();

        InitializeColumnVisibility();
    }

    /// <summary>
    /// Backs the "Choose columns" flyout; one entry per toggleable column of <see cref="DataGrid"/>.
    /// </summary>
    public ObservableCollection<ColumnItem> ColumnItems { get; } = new();

    private void InitializeColumnVisibility()
    {
        var hiddenColumns = new HashSet<string>(
            GeneralOptions.Instance.HiddenColumns.Split(',').Where(header => !string.IsNullOrEmpty(header)),
            StringComparer.OrdinalIgnoreCase);

        // Package is the primary identity column and the padding column is a layout helper;
        // neither should show up in the column chooser.
        var toggleableColumns = DataGrid.Columns
            .Where(column => column != PackageColumn && column != PaddingColumn);

        foreach (var column in toggleableColumns)
        {
            var header = column.Header?.ToString();

            if (header is not null && hiddenColumns.Contains(header))
            {
                column.Visibility = Visibility.Collapsed;
            }

            ColumnItems.Add(new ColumnItem(column, PersistColumnVisibility));
        }
    }

    private void PersistColumnVisibility()
    {
        var hiddenColumns = DataGrid.Columns
            .Where(column => column.Visibility != Visibility.Visible)
            .Select(column => column.Header?.ToString())
            .ExceptNullItems();

        GeneralOptions.Instance.HiddenColumns = string.Join(",", hiddenColumns);
        GeneralOptions.Instance.Save();
    }
}
