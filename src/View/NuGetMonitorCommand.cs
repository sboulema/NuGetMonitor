using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.View;

internal sealed class NuGetMonitorCommand
{
    private const int _commandId = 0x0100;

    private static readonly Guid _commandSet = new("df4cd5dd-21c1-4666-8b25-bffe33b47ac1");

    private readonly AsyncPackage _package;

    private NuGetMonitorCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandId = new CommandID(_commandSet, _commandId);
        var menuItem = new MenuCommand(Execute, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    public static NuGetMonitorCommand? Instance
    {
        get;
        private set;
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as OleMenuCommandService ?? throw new InvalidOperationException("Failed to get menu command service");

        Instance = new NuGetMonitorCommand(package, commandService);
    }

    public void ShowToolWindow()
    {
        _package.JoinableTaskFactory.RunAsync(async delegate
        {
            var window = await _package.ShowToolWindowAsync(typeof(NuGetMonitorToolWindow), 0, true, _package.DisposalToken);
            if (null == window || null == window.Frame)
            {
                throw new NotSupportedException("Cannot create tool window");
            }
        }).FireAndForget();
    }

    private void Execute(object sender, EventArgs e)
    {
        ShowToolWindow();
    }
}