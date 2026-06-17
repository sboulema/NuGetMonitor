using NuGetMonitor.Model;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Model.Services;

namespace NuGetMonitor.Services;

internal sealed class MonitorService
{
    private readonly InfoBarService _infoBarService;

    public MonitorService(InfoBarService infoBarService)
    {
        _infoBarService = infoBarService;
    }

    public void RegisterEventHandlers()
    {
        PlatformAbstractions.SolutionOpened += OnSolutionOpened;
        PlatformAbstractions.SolutionClosed += OnSolutionClosed;
    }

    public void UnregisterEventHandlers()
    {
        PlatformAbstractions.SolutionOpened -= OnSolutionOpened;
        PlatformAbstractions.SolutionClosed -= OnSolutionClosed;
    }

    public void CheckForUpdates()
    {
        CheckForUpdatesInternal().FireAndForget();
    }

    private void OnSolutionClosed(object? sender, EventArgs e)
    {
        Reset(null);
    }

    private void OnSolutionOpened(object? sender, EventArgs e)
    {
        CheckForUpdates();
    }

    private void Reset(string? solutionFolder)
    {
        _infoBarService.ClearMessages();
        NuGetService.Reset(solutionFolder);
        ProjectService.ClearCache();
    }

    private async Task CheckForUpdatesInternal()
    {
        try
        {
            Reset(PlatformAbstractions.SolutionPath);

            var projectPaths = await PlatformAbstractions.GetProjectFilePaths();

            if (projectPaths.Count == 0)
            {
                Log("No projects found in solution");
                return;
            }

            Log($"Found {projectPaths.Count} projects");
            Log("Check top level packages");

            var packageReferences = await ProjectService.GetPackageReferences(projectPaths);

            var topLevelPackages = await NuGetService.CheckPackageReferences(packageReferences);

            Log($"{topLevelPackages.Count} packages found");

            if (topLevelPackages.Count == 0)
                return;

            ShowTopLevelPackageIssues(topLevelPackages);

            Log("Check transitive packages");

            var transitiveDependencies = await NuGetService.GetTransitiveDependencies(topLevelPackages);

            ShowTransitivePackageIssues(transitiveDependencies);

            LogRedundantPackageReferences(transitiveDependencies);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ObjectDisposedException))
        {
            Log(LogLevel.Error, $"Check for updates failed: {ex}");
        }
    }

    private void ShowTopLevelPackageIssues(IEnumerable<PackageReferenceInfo> topLevelPackages)
    {
        var message = string.Join(", ", GetInfoTexts(topLevelPackages).ExceptNullItems());

        if (string.IsNullOrEmpty(message))
        {
            Log("No issues found");
            return;
        }

        Log(message);
        _infoBarService.ShowMessage($"{message}.");
    }

    private void ShowTransitivePackageIssues(ICollection<TransitiveDependencies> transitiveDependencies)
    {
        var transitivePackages = transitiveDependencies
            .SelectMany(dependency => dependency.TransitivePackages)
            .Distinct()
            .ToArray();

        Log($"{transitivePackages.Length} transitive packages found");

        var vulnerablePackages = transitivePackages.Where(item => item.IsVulnerable && item.VulnerabilityMitigation.IsNullOrEmpty()).ToArray();

        if (vulnerablePackages.Length <= 0)
        {
            Log("No transitive package issues found");
            return;
        }

        var packageInfo = string.Join(", ", vulnerablePackages.Select(package => package.PackageIdentity));
        var vulnerabilityDescription = vulnerablePackages.CountedDescription("vulnerability");
        var message = $"{vulnerabilityDescription} in transitive dependencies: {packageInfo}";

        Log(message);
        _infoBarService.ShowMessage(message);
    }

    private static void LogRedundantPackageReferences(ICollection<TransitiveDependencies> transitiveDependencies)
    {
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

    private static IEnumerable<string?> GetInfoTexts(IEnumerable<PackageReferenceInfo> topLevelPackageInfos)
    {
        var topLevelPackages = topLevelPackageInfos
            .Where(item => item.PackageReferenceEntries.Any(entry => NuGet.Versioning.NuGetVersion.TryParse(entry.Identity.VersionRange.OriginalString, out _)))
            .Select(item => new { item.PackageInfo, IsPinned = item.PackageReferenceEntries.All(entry => entry.IsPinned) })
            .ToArray();

        yield return topLevelPackages.CountedDescription("update", item => item.PackageInfo.IsOutdated && !item.IsPinned);
        yield return topLevelPackages.CountedDescription("deprecation", item => item.PackageInfo.IsDeprecated && !item.IsPinned);
        yield return topLevelPackages.CountedDescription("vulnerability", item => item.PackageInfo.IsVulnerable && item.PackageInfo.VulnerabilityMitigation.IsNullOrEmpty());
    }
}
