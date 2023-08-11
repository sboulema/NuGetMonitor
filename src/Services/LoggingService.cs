using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.Services;

internal static class LoggingService
{
    private static Guid _outputPaneGuid = new("{5B951352-356E-45A9-8F73-80DF1C57FED4}");

    public static async Task Log(string message)
    {
        var outputWindow = await VS.Services.GetOutputWindowAsync().ConfigureAwait(false);

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var errorCode = outputWindow.GetPane(ref _outputPaneGuid, out var pane);

        if (ErrorHandler.Failed(errorCode) || pane == null)
        {
            outputWindow.CreatePane(ref _outputPaneGuid, "NuGet Monitor", Convert.ToInt32(true), Convert.ToInt32(false));
            outputWindow.GetPane(ref _outputPaneGuid, out pane);
        }

        pane?.OutputStringThreadSafe($"[{DateTime.Now:T}] {message}\r\n");
    }
}