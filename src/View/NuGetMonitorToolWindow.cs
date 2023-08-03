using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;

namespace NuGetMonitor.View;

/// <summary>
/// This class implements the tool window exposed by this package and hosts a user control.
/// </summary>
/// <remarks>
/// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
/// usually implemented by the package implementer.
/// <para>
/// This class derives from the ToolWindowPane class provided from the MPF in order to use its
/// implementation of the IVsUIElementPane interface.
/// </para>
/// </remarks>
[Guid("6ce47eec-3296-48f5-9dec-8883a276a7c8")]
public sealed class NuGetMonitorToolWindow : ToolWindowPane
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetMonitorToolWindow"/> class.
    /// </summary>
    public NuGetMonitorToolWindow() : base(null)
    {
        Caption = "NuGet Monitor";

        // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
        // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
        // the object returned by the Content property.
        Content = new NuGetMonitorControl();
    }
}