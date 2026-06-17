using System.Windows;

namespace NuGetMonitor.Services;

internal static class ClipboardService
{
    public static void SetText(string text)
    {
        Clipboard.SetText(text);
    }
}
