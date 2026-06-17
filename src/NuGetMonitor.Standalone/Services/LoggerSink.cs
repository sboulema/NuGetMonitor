using System.Collections.ObjectModel;
using NuGetMonitor.Model.Abstractions;
using NuGetMonitor.Model.Services;
using static Avalonia.Threading.Dispatcher;

namespace NuGetMonitor.Services;

internal sealed class LoggerSink : ILoggerSink
{
    private readonly ObservableCollection<LogEntry> _logEntries = [];

    public ReadOnlyObservableCollection<LogEntry> LogEntries { get; }

    public LoggerSink()
    {
        LogEntries = new(_logEntries);
    }

    public void Log(LogLevel logLevel, string message)
    {
        var entry = new LogEntry(DateTime.Now, logLevel, message);

        // Ensure we're on the UI thread for the ObservableCollection
        if (UIThread.CheckAccess())
        {
            _logEntries.Add(entry);
        }
        else
        {
            UIThread.Post(() => _logEntries.Add(entry));
        }
    }
}

internal sealed record LogEntry(DateTime Timestamp, LogLevel LogLevel, string Message)
{
    public string FormattedMessage => $"[{Timestamp:T}, {LogLevel}] {Message}";
}
