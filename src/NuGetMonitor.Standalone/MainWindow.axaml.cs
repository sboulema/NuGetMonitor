using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PropertyChanged;

namespace NuGetMonitor;

[DoNotNotify]
internal sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Subscribe to input events to invalidate command states
        AddHandler(PointerPressedEvent, OnInputReceived, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnInputReceived, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnInputReceived, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnInputReceived, RoutingStrategies.Tunnel);
    }

    private void OnInputReceived(object? sender, RoutedEventArgs e)
    {
        // Invalidate command states on any input
        CommandManager.InvalidateRequerySuggested();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // Called by the BrowseCommand relay — we intercept it here because file dialogs need a Window reference.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (ViewModel is { } viewModel)
        {
            viewModel.BrowseRequested += OnBrowseRequested;
            viewModel.OpenRecentRequested += OnOpenRecentRequested;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (ViewModel is { } viewModel)
        {
            viewModel.BrowseRequested -= OnBrowseRequested;
            viewModel.OpenRecentRequested -= OnOpenRecentRequested;
        }
    }

    private void OnBrowseRequested(object? sender, EventArgs e)
    {
        OpenSolutionFileAsync().ConfigureAwait(false);
    }

    private void OnOpenRecentRequested(object? sender, string path)
    {
        RecentButton.Flyout?.Hide();
        ViewModel?.LoadSolutionAsync(path).FireAndForget();
    }

    private async Task OpenSolutionFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Solution File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Visual Studio Solution") { Patterns = ["*.sln"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && ViewModel is { } viewModel)
            {
                await viewModel.LoadSolutionAsync(path);
            }
        }
    }
}
