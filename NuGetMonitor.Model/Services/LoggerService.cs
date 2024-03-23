using NuGetMonitor.Model.Abstractions;

namespace NuGetMonitor.Model.Services;

public enum LogLevel
{
    Error,
    Warning,
    Info
}

public static class LoggerService
{
    private static readonly List<ILoggerSink> _sinks = new();

    public static void Log(string message)
    {
        Log(LogLevel.Info, message);

    }

    public static void Log(LogLevel severity, string message)
    {
        foreach (var sink in _sinks)
        {
            sink.Log(severity, message);
        }
    }

    public static void AddSink(ILoggerSink sink)
    {
        _sinks.Add(sink);
    }
}