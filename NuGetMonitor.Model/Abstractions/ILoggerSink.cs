using NuGetMonitor.Model.Services;

namespace NuGetMonitor.Model.Abstractions
{
    public interface ILoggerSink
    {
        void Log(LogLevel severity, string message);
    }
}
