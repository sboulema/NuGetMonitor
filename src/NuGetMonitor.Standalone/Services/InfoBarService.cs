using System.Collections.ObjectModel;
using static Avalonia.Threading.Dispatcher;

namespace NuGetMonitor.Services;

internal sealed class InfoBarService
{
    private readonly ObservableCollection<InfoBarMessage> _messages = [];

    public ReadOnlyObservableCollection<InfoBarMessage> Messages { get; }

    public static readonly InfoBarService Instance = new();

    private InfoBarService()
    {
        Messages = new(_messages);
    }

    public void ShowMessage(string message)
    {
        var infoBarMessage = new InfoBarMessage(message);

        // Ensure we're on the UI thread for the ObservableCollection
        if (UIThread.CheckAccess())
        {
            _messages.Add(infoBarMessage);
        }
        else
        {
            UIThread.Post(() => _messages.Add(infoBarMessage));
        }
    }

    public void ClearMessages()
    {
        if (UIThread.CheckAccess())
        {
            _messages.Clear();
        }
        else
        {
            UIThread.Post(() => _messages.Clear());
        }
    }
}

internal sealed record InfoBarMessage(string Message)
{
    public string FormattedMessage => $"{Message}";
}
