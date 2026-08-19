using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NuGetMonitor.Model.Services;
using NuGetMonitor.Services;
using NuGetMonitor.View.Monitor;

namespace NuGetMonitor;

internal sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly LoggerSink _loggerSink;
    private readonly InfoBarService _infoBarService = InfoBarService.Instance;
    private readonly MonitorService _monitorService;
    private readonly Settings _settings = Settings.Instance;

    public string? SolutionPath { get; private set; }

    public string? StatusMessage { get; private set; }

    public object? NuGetMonitorViewModel { get; } = new NuGetMonitorViewModel();

    public ReadOnlyObservableCollection<LogEntry> LogEntries => _loggerSink.LogEntries;

    public ReadOnlyObservableCollection<InfoBarMessage> InfoBarMessages => _infoBarService.Messages;

    public ObservableCollection<string> RecentSolutions { get; } = [];

    public ICommand BrowseCommand => new DelegateCommand(() => BrowseRequested?.Invoke(this, EventArgs.Empty));

    public ICommand OpenRecentCommand => new DelegateCommand<string>(path => OpenRecentRequested?.Invoke(this, path));

    internal EventHandler? BrowseRequested;
    internal EventHandler<string>? OpenRecentRequested;

    public MainViewModel()
    {
        _loggerSink = new LoggerSink();
        LoggerService.AddSink(_loggerSink);

        _monitorService = new MonitorService(_infoBarService);
        _monitorService.RegisterEventHandlers();

        RecentSolutions.AddRange(_settings.RecentSolutions);
    }

    internal async Task LoadSolutionAsync(string? path)
    {
        SolutionPath = path;
        StatusMessage = string.IsNullOrEmpty(path) ? "No solution loaded" : $"Loaded: {path}";

        if (!string.IsNullOrEmpty(path))
        {
            _settings.AddRecentSolution(path);

            RecentSolutions.Clear();
            RecentSolutions.AddRange(_settings.RecentSolutions);
        }

        PlatformAbstractions.OpenSolution(path);

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _monitorService.UnregisterEventHandlers();
    }
}
