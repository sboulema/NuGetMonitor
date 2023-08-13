using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Models;

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

            var solution = await VS.Solutions.GetCurrentSolutionAsync().ConfigureAwait(true);

            if (solution is null)
                return;

            await LoggingService.Log($"Solution: {solution.Name}").ConfigureAwait(true);

            await LoggingService.Log("Check top level packages").ConfigureAwait(true);

            var packageReferences = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences).ConfigureAwait(true);

            await LoggingService.Log($"{topLevelPackages.Count} packages found").ConfigureAwait(true);

            InfoBarService.ShowTopLevelPackageIssues(topLevelPackages);

            await LoggingService.Log("Check transitive packages").ConfigureAwait(true);

            var transitivePackages = await NuGetService.GetTransitivePackages(packageReferences, topLevelPackages).ConfigureAwait(true);

            await LoggingService.Log($"{transitivePackages.Count} transitive packages found").ConfigureAwait(true);

            InfoBarService.ShowTransitivePackageIssues(transitivePackages, topLevelPackages);
        }
        catch (Exception ex)
        {
            await LoggingService.Log($"Check for updates failed: {ex}").ConfigureAwait(false);
        }
    }
}