using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.View.Monitor;


[Guid(Id)]
public sealed class NuGetMonitorToolWindow : ToolWindowPane
{
    public const string Id = "6ce47eec-3296-48f5-9dec-8883a276a7c8";

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetMonitorToolWindow"/> class.
    /// </summary>
    public NuGetMonitorToolWindow() : base(null)
    {
        Caption = "NuGet Monitor";
        Content = new NuGetMonitorControl();
    }
}