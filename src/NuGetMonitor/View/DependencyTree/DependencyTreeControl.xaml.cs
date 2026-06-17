namespace NuGetMonitor.View.DependencyTree;

/// <summary>
/// Interaction logic for DependencyTreeControl.xaml
/// </summary>
public sealed partial class DependencyTreeControl
{
    public DependencyTreeControl()
    {
        InitializeComponent();

        DataContext = new DependencyTreeViewModel();
    }
}