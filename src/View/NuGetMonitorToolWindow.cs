using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.View;


[Guid("6ce47eec-3296-48f5-9dec-8883a276a7c8")]
public sealed class NuGetMonitorToolWindow : ToolWindowPane
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetMonitorToolWindow"/> class.
    /// </summary>
    public NuGetMonitorToolWindow() : base(null)
    {
        Caption = "NuGet Monitor";
        Content = new NuGetMonitorControl();
    }
}