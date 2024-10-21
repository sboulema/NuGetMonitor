using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.View.DependencyTree;

[Guid("C82FB9BC-D58C-48CA-95EC-40905527089F")]
public sealed class DependencyTreeToolWindow : ToolWindowPane
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyTreeToolWindow"/> class.
    /// </summary>
    public DependencyTreeToolWindow() : base(null)
    {
        Caption = "Package Dependency Tree";
        Content = new DependencyTreeControl();
    }
}