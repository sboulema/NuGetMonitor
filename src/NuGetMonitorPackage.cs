using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Options;
using NuGetMonitor.Services;
using NuGetMonitor.View;

namespace NuGetMonitor;

[Guid(PackageGuidString)]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(NuGetMonitorToolWindow))]
[ProvideOptionPage(typeof(GeneralOptions), "NuGet Monitor", "General", 0, 0, true)]
[ProvideProfile(typeof(GeneralOptions), "NuGet Monitor", "General", 0, 0, true)]
public sealed class NuGetMonitorPackage : ToolkitPackage
{
    public const string PackageGuidString = "38279e01-6b27-4a29-9221-c4ea8748f16e";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        MonitorService.RegisterEventHandler();

        MonitorService.CheckForUpdates();

        await NuGetMonitorCommand.InitializeAsync(this);
    }
}