using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace NuGetMonitor.Services;

internal static class MonitorService
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

            await LoggingService.LogAsync($"Solution: {solution.Name}").ConfigureAwait(true);

            await LoggingService.LogAsync("Check top level packages").ConfigureAwait(true);

            var packageReferences = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences).ConfigureAwait(true);

            await LoggingService.LogAsync($"{topLevelPackages.Count} packages found").ConfigureAwait(true);

            if (topLevelPackages.Count == 0)
                return;

            InfoBarService.ShowTopLevelPackageIssues(topLevelPackages);

            await LoggingService.LogAsync("Check transitive packages").ConfigureAwait(true);

            var transitiveDependencies = await NuGetService.GetTransitivePackages(packageReferences, topLevelPackages).ConfigureAwait(true);

            InfoBarService.ShowTransitivePackageIssues(transitiveDependencies);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException))
        {
            await LoggingService.LogAsync($"Check for updates failed: {ex}");
        }
    }
}