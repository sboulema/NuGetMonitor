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
    {
        InfoBarService.CloseInfoBars();
        NuGetService.ClearCache();
    }

    private static void SolutionEvents_OnAfterOpenSolution(Solution? solution)
        => CheckForUpdates().FireAndForget();

    public static async Task CheckForUpdates()
    {
        try
        {
            var topLevelPackages = await NuGetService.CheckPackageReferences().ConfigureAwait(true);

            InfoBarService.ShowTopLevelPackageIssues(topLevelPackages).FireAndForget();

            var transitivePackages = await NuGetService.GetTransitivePackages(topLevelPackages).ConfigureAwait(true);

            InfoBarService.ShowTransitivePackageIssues(transitivePackages).FireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}