using System.Windows;

namespace NuGetMonitor;

/// <summary>
/// Interaction logic for NugetMonitorControl.xaml
/// </summary>
public partial class NuGetMonitorControl
{
    public NuGetMonitorControl()
    {
        InitializeComponent();
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Click");
    }
}