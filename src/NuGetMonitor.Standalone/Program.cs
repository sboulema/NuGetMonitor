using Avalonia;
using Microsoft.Build.Locator;

namespace NuGetMonitor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RegisterMsBuild();
        StartApp(args);
    }

    private static void RegisterMsBuild()
    {
        if (MSBuildLocator.IsRegistered)
            return;

        MSBuildLocator.RegisterDefaults();
    }

    private static void StartApp(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
