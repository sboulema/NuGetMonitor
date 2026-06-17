using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NuGetMonitor.Services;
using PropertyChanged;

namespace NuGetMonitor;

[DoNotNotify]
internal sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var solutionPath = args.Length > 0 ? args[0] : null;

            var mainViewModel = new MainViewModel();

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktop.MainWindow = mainWindow;

            var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
            if (clipboard is not null)
                ClipboardService.Initialize(clipboard);

            mainViewModel.LoadSolutionAsync(solutionPath).FireAndForget();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
