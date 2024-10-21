using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGetMonitor.Model.Abstractions;
using NuGetMonitor.Model.Services;

namespace NuGetMonitor.Services;

internal sealed class OutputWindowLoggingSink : ILoggerSink
{
    private static Guid _outputPaneGuid = new("{5B951352-356E-45A9-8F73-80DF1C57FED4}");

    private static IVsOutputWindowPane? _outputWindowPane;

    public void Log(LogLevel logLevel, string message)
    {
        LogAsync(logLevel, message).FireAndForget();
    }

    private static async Task LogAsync(LogLevel logLevel, string message)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync();

        _outputWindowPane ??= GetOutputWindowPane();
        _outputWindowPane?.OutputStringThreadSafe($"[{DateTime.Now:T}, {logLevel}] {message}\r\n");
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