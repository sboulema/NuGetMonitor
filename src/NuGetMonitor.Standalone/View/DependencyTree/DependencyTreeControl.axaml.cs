using PropertyChanged;

namespace NuGetMonitor.View.DependencyTree;

[DoNotNotify]
public sealed partial class DependencyTreeControl : UserControl
{
    public DependencyTreeControl()
    {
        InitializeComponent();

        DataContext = new DependencyTreeViewModel();
    }
}
