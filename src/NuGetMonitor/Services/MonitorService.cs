using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Model.Services;
using TomsToolbox.Essentials;

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
        NuGetService.Reset(VS.Solutions.GetCurrentSolution()?.FullPath);
        ProjectService.ClearCache();
    }

    private static void SolutionEvents_OnAfterOpenSolution(Solution? solution) => CheckForUpdates();

    private static async Task CheckForUpdatesInternal()
    {
        try
        {
            Reset();

            var solution = await VS.Solutions.GetCurrentSolutionAsync();

            if (solution is null)
                return;

            Log($"Solution: {solution.Name}");

            Log("Check top level packages");

            var projects = await VS.Solutions.GetAllProjectsAsync();

            var projectPaths = projects.Select(project => project.FullPath)
                .ExceptNullItems()
                .ToArray();

            var packageReferences = await ProjectService.GetPackageReferences(projectPaths);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences);

            Log($"{topLevelPackages.Count} packages found");

            if (topLevelPackages.Count == 0)
                return;

            InfoBarService.ShowTopLevelPackageIssues(topLevelPackages);

            Log("Check transitive packages");

            var transitiveDependencies = await NuGetService.GetTransitiveDependencies(topLevelPackages);

            InfoBarService.ShowTransitivePackageIssues(transitiveDependencies);

            foreach (var (project, packageInfos, inheritedDependencies, _) in transitiveDependencies)
            {
                var redundantDependencies = packageInfos
                    .Where(item => inheritedDependencies.TryGetValue(item.PackageIdentity.Id, out var inherited) && inherited.PackageIdentity.Version >= item.PackageIdentity.Version)
                    .ToArray();

                if (redundantDependencies.Length <= 0)
                    continue;

                Log($"Project {project.NameAndFramework} has {redundantDependencies.Length} potentially redundant dependencies: {string.Join(", ", redundantDependencies.Select(item => item.PackageIdentity.Id))}");
            }
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException))
        {
            Log($"Check for updates failed: {ex}");
        }
    }
}