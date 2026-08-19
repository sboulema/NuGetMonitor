using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PropertyChanged;

namespace NuGetMonitor.View.Monitor;

/// <summary>
/// Represents a single column of the packages <see cref="DataGrid"/>, exposed as a checkable item
/// in the "Choose columns" flyout so the user can show or hide it.
/// </summary>
public sealed class ColumnItem : INotifyPropertyChanged
{
    private readonly DataGridColumn _column;
    private readonly Action _visibilityChanged;

    public ColumnItem(DataGridColumn column, Action visibilityChanged)
    {
        _column = column;
        _visibilityChanged = visibilityChanged;

        Header = column.Header?.ToString() ?? string.Empty;
        IsVisible = column.Visibility == Visibility.Visible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Header { get; }

    [OnChangedMethod(nameof(OnIsVisibleChanged))]
    public bool IsVisible { get; set; }

    private void OnIsVisibleChanged()
    {
        _column.Visibility = IsVisible ? Visibility.Visible : Visibility.Collapsed;

        _visibilityChanged();
    }
}
