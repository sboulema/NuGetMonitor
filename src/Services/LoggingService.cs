using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace NuGetMonitor.Services;

internal static class LoggingService
{
    private static Guid _outputPaneGuid = new("{5B951352-356E-45A9-8F73-80DF1C57FED4}");

    private static IVsOutputWindowPane? _outputWindowPane;

    public static void Log(string message)
    {
        LogAsync(message).FireAndForget();
    }

    public static async Task LogAsync(string message)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync();

        _outputWindowPane ??= GetOutputWindowPane();
        _outputWindowPane?.OutputStringThreadSafe($"[{DateTime.Now:T}] {message}\r\n");
    }

    private static IVsOutputWindowPane? GetOutputWindowPane()
    {
        ThrowIfNotOnUIThread();

        var outputWindow = VS.GetRequiredService<SVsOutputWindow, IVsOutputWindow>();

        var errorCode = outputWindow.GetPane(ref _outputPaneGuid, out var pane);

        if (!ErrorHandler.Failed(errorCode) && pane != null)
            return pane;

        outputWindow.CreatePane(ref _outputPaneGuid, "NuGet Monitor", Convert.ToInt32(true), Convert.ToInt32(false));
        outputWindow.GetPane(ref _outputPaneGuid, out pane);

        return pane;
    }
}