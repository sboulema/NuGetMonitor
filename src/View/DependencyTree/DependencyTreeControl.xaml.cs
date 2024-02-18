using NuGetMonitor.Services;

namespace NuGetMonitor.View.DependencyTree
{
    /// <summary>
    /// Interaction logic for DependencyTreeControl.xaml
    /// </summary>
    public partial class DependencyTreeControl
    {
        public DependencyTreeControl()
        {
            InitializeComponent();

            DataContext = new DependencyTreeViewModel(SolutionService.Instance);
        }
    }
}