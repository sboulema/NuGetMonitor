using Microsoft.VisualStudio.Shell;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace NuGetMonitor.View;

/// <summary>
/// Command handler
/// </summary>
internal sealed class NuGetMonitorCommand
{
    /// <summary>
    /// Command ID.
    /// </summary>
    public const int CommandId = 0x0100;

    /// <summary>
    /// Command menu group (command set GUID).
    /// </summary>
    public static readonly Guid CommandSet = new("df4cd5dd-21c1-4666-8b25-bffe33b47ac1");

    /// <summary>
    /// VS Package that provides this command, not null.
    /// </summary>
    private readonly AsyncPackage _package;

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetMonitorCommand"/> class.
    /// Adds our command handlers for menu (commands must exist in the command table file)
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    /// <param name="commandService">Command service to add command to, not null.</param>
    private NuGetMonitorCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandId = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(Execute, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    /// <summary>
    /// Gets the instance of the command.
    /// </summary>
    public static NuGetMonitorCommand? Instance
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the service provider from the owner package.
    /// </summary>
    private IAsyncServiceProvider ServiceProvider
    {
        get
        {
            return _package;
        }
    }

    /// <summary>
    /// Initializes the singleton instance of the command.
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    public static async Task InitializeAsync(AsyncPackage package)
    {
        // Switch to the main thread - the call to AddCommand in NuGetMonitorCommand's constructor requires
        // the UI thread.
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as OleMenuCommandService ?? throw new InvalidOperationException("Failed to get menu command service");
        Instance = new NuGetMonitorCommand(package, commandService);
    }

    public void ShowToolWindow()
    {
        _package.JoinableTaskFactory.RunAsync(async delegate
        {
            var window = await _package.ShowToolWindowAsync(typeof(NuGetMonitorToolWindow), 0, true, _package.DisposalToken).ConfigureAwait(false);
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