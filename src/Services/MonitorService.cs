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

    public static void CheckForUpdates()
    {
        CheckForUpdatesInternal().FireAndForget();
    }

    private static void SolutionEvents_OnAfterCloseSolution()
    {
        Reset();
    }

    private static void Reset()
    {
        InfoBarService.CloseInfoBars();
        NuGetService.ClearCache();
        ProjectService.ClearCache();
    }

    private static void SolutionEvents_OnAfterOpenSolution(Solution? solution) => CheckForUpdates();

    private static async Task CheckForUpdatesInternal()
    {
        try
        {
            Reset();

            var topLevelPackages = await NuGetService.CheckPackageReferences().ConfigureAwait(true);

            InfoBarService.ShowTopLevelPackageIssues(topLevelPackages);

            var transitivePackages = await NuGetService.GetTransitivePackages(topLevelPackages).ConfigureAwait(true);

            InfoBarService.ShowTransitivePackageIssues(transitivePackages, topLevelPackages);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}