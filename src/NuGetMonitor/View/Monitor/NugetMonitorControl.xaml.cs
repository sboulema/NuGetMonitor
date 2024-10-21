using NuGetMonitor.Services;

namespace NuGetMonitor.View.Monitor;

/// <summary>
/// Interaction logic for NugetMonitorControl.xaml
/// </summary>
public sealed partial class NuGetMonitorControl
{
    public NuGetMonitorControl()
    {
        InitializeComponent();

        DataContext = new NuGetMonitorViewModel(SolutionService.Instance);
    }
}