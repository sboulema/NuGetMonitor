using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.View.DependencyTree;
using NuGetMonitor.View.Monitor;

namespace NuGetMonitor;

internal sealed class NuGetMonitorCommands
{
    private const int _monitorCommandId = 0x0100;
    private const int _dependencyTreeCommandId = 0x0101;

    private static readonly Guid _commandSet = new("df4cd5dd-21c1-4666-8b25-bffe33b47ac1");

    private readonly AsyncPackage _package;

    private NuGetMonitorCommands(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        commandService.AddCommand(new MenuCommand(ExecuteMonitorCommand, new CommandID(_commandSet, _monitorCommandId)));
        commandService.AddCommand(new MenuCommand(ExecuteDependencyTreeCommand, new CommandID(_commandSet, _dependencyTreeCommandId)));
    }

    public static NuGetMonitorCommands? Instance
    {
        get;
        private set;
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as OleMenuCommandService ?? throw new InvalidOperationException("Failed to get menu command service");

        Instance = new NuGetMonitorCommands(package, commandService);
    }

    public void ShowMonitorToolWindow()
    {
        _package.JoinableTaskFactory.RunAsync(async delegate
        {
            var window = await _package.ShowToolWindowAsync(typeof(NuGetMonitorToolWindow), 0, true, _package.DisposalToken);
            if (window?.Frame == null)
                throw new NotSupportedException("Cannot create tool window");
        }).FireAndForget();
    }

    private void ExecuteMonitorCommand(object sender, EventArgs e)
    {
        ShowMonitorToolWindow();
    }
    public void ShowDependencyTreeToolWindow()
    {
        _package.JoinableTaskFactory.RunAsync(async delegate
        {
            var window = await _package.ShowToolWindowAsync(typeof(DependencyTreeToolWindow), 0, true, _package.DisposalToken);
            if (window?.Frame == null)
                throw new NotSupportedException("Cannot create tool window");
        }).FireAndForget();
    }

    private void ExecuteDependencyTreeCommand(object sender, EventArgs e)
    {
        ShowDependencyTreeToolWindow();
    }
}