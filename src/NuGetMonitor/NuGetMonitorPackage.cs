using System.Reflection;
using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using NuGetMonitor.Model.Services;
using NuGetMonitor.Options;
using NuGetMonitor.Services;
using NuGetMonitor.View.DependencyTree;
using NuGetMonitor.View.Monitor;

namespace NuGetMonitor;

[Guid(PackageGuidString)]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(NuGetMonitorToolWindow))]
[ProvideToolWindow(typeof(DependencyTreeToolWindow))]
[ProvideOptionPage(typeof(GeneralOptionsPage), "NuGet Monitor", "General", 0, 0, true)]
[ProvideProfile(typeof(GeneralOptionsPage), "NuGet Monitor", "General", 0, 0, true)]
public sealed class NuGetMonitorPackage : ToolkitPackage
{
    public const string PackageGuidString = "38279e01-6b27-4a29-9221-c4ea8748f16e";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        try
        {
            // Workaround for "Could not load file or assembly 'NuGetMonitor.Model'"
            // Even though it's listed in AssemblyInfo with ProvideCodeBase, VS fails to load it when the package is loaded.
            // This forces it to be loaded before any types from it are used.
            Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "NuGetMonitor.Model.dll"));

            await InitializeInternalAsync(cancellationToken);
        }
        catch (ReflectionTypeLoadException ex)
        {
            await VS.MessageBox.ShowAsync("NuGetMonitor: An error occurred during initialization: " + ex.Message, string.Join(Environment.NewLine, ex.LoaderExceptions.Select(le => le.Message)));
        }
        catch (Exception ex)
        {
            await VS.MessageBox.ShowAsync("NuGetMonitor: An error occurred during initialization: " + ex);
        }
    }

    private async Task InitializeInternalAsync(CancellationToken cancellationToken)
    {
        LoggerService.AddSink(new OutputWindowLoggingSink());

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        MonitorService.RegisterEventHandler();

        MonitorService.CheckForUpdates();

        await NuGetMonitorCommands.InitializeAsync(this);
    }
}