using System.Diagnostics;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.Services;

public static class MonitorService
{
    public static void RegisterEventHandler()
    {
        VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        VS.Events.SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
        VS.Events.ShellEvents.ShutdownStarted += NuGetService.Shutdown;
    }

    private static void SolutionEvents_OnAfterCloseSolution()
        => InfoBarService.CloseInfoBar();

    private static void SolutionEvents_OnAfterOpenSolution(Solution? solution)
        => CheckForUpdates().FireAndForget();

    public static async Task CheckForUpdates()
    {
        try
        {
            var packageIdentities = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var packageReferences = await NuGetService.CheckPackageReferences(packageIdentities).ConfigureAwait(true);

            InfoBarService.ShowInfoBar(packageReferences.ToArray()).FireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}