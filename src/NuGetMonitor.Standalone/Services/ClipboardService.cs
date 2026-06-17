using Avalonia.Input.Platform;

namespace NuGetMonitor.Services;

internal static class ClipboardService
{
    private static IClipboard? _clipboard;

    public static void Initialize(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public static void SetText(string text)
    {
        _clipboard?.SetTextAsync(text).FireAndForget();
    }
}
