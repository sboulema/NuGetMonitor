using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Versioning;
using NuGetMonitor.Model;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Options;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

internal static class InfoBarService
{
    private static readonly List<InfoBar> _infoBars = new();

    private enum Actions
    {
        Manage,
        ShowDependencyTree
    }

    public static void ShowTopLevelPackageIssues(IEnumerable<PackageReferenceInfo> topLevelPackages)
    {
        var message = string.Join(", ", GetInfoTexts(topLevelPackages).ExceptNullItems());

        if (string.IsNullOrEmpty(message))
        {
            Log("No issues found");
            return;
        }

        Log(message);

        var textSpans = new[]
        {
            new InfoBarTextSpan($"{message}. "),
            new InfoBarHyperlink("Manage", Actions.Manage),
            new InfoBarTextSpan(" packages.")
        };

        ShowInfoBar(textSpans).FireAndForget();
    }

    public static void ShowTransitivePackageIssues(ICollection<TransitiveDependencies> transitiveDependencies)
    {
        if (!GeneralOptions.Instance.ShowTransitivePackagesIssues)
            return;

        var transitivePackages = transitiveDependencies
            .SelectMany(dependency => dependency.TransitivePackages)
            .Distinct()
            .ToArray();

        Log($"{transitivePackages.Length} transitive packages found");

        var vulnerablePackages = transitivePackages.Where(item => item.IsVulnerable && item.VulnerabilityMitigation.IsNullOrEmpty()).ToArray();

        if (vulnerablePackages.Length <= 0)
        {
            Log("No issues found");
            return;
        }

        var packageInfo = string.Join("\r\n- ", vulnerablePackages.Select(package => package.PackageIdentity));
        var message = $"{vulnerablePackages.CountedDescription("vulnerability")} in transitive dependencies:\r\n- {packageInfo}\r\n";

        Log(message);

        var textSpans = new[]
        {
            new InfoBarTextSpan(message),
            new InfoBarHyperlink("Open dependency tree", Actions.ShowDependencyTree)
        };

        ShowInfoBar(textSpans).FireAndForget();
    }

    private static async Task ShowInfoBar(IEnumerable<InfoBarTextSpan> textSpans, TimeSpan? timeOut = default)
    {
        var model = new InfoBarModel(textSpans, KnownMonikers.NuGet, isCloseButtonVisible: true);

        var infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model) ?? throw new InvalidOperationException("Failed to create the info bar");
        infoBar.ActionItemClicked += InfoBar_ActionItemClicked;

        _infoBars.Add(infoBar);

        await infoBar.TryShowInfoBarUIAsync();

        if (timeOut.HasValue)
        {
            await Task.Delay(timeOut.Value);
            infoBar.Close();
            _infoBars.Remove(infoBar);
        }
    }

    public static void CloseInfoBars()
    {
        _infoBars.ForEach(item => item.Close());
        _infoBars.Clear();
    }

    private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
    {
        ThrowIfNotOnUIThread();

        switch (e.ActionItem.ActionContext)
        {
            case Actions.Manage:
                OpenNuGetPackageManager();
                break;

            case Actions.ShowDependencyTree:
                OpenDependencyTree();
                break;
        }

        if (GeneralOptions.Instance.CloseInfoBar)
        {
            (sender as InfoBar)?.Close();
        }
    }

    private static void OpenNuGetPackageManager()
    {
        if (GeneralOptions.Instance.OpenNuGetPackageManager)
        {
            VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
        }
        else
        {
            NuGetMonitorCommands.Instance?.ShowMonitorToolWindow();
        }
    }

    private static void OpenDependencyTree()
    {
        NuGetMonitorCommands.Instance?.ShowDependencyTreeToolWindow();
    }

    private static IEnumerable<string?> GetInfoTexts(IEnumerable<PackageReferenceInfo> topLevelPackageInfos)
    {
        // Idea for showing counts, not sure if unicode icons in a InfoBar feel native
        // new InfoBarTextSpan($"NuGet update: 🔼 {outdatedCount} ⚠ {deprecatedCount} 💀 {vulnerableCount}. "),

        var topLevelPackages = topLevelPackageInfos
            .Where(item => item.PackageReferenceEntries.Any(entry => NuGetVersion.TryParse(entry.Identity.VersionRange.OriginalString, out _)))
            .Select(item => new { item.PackageInfo, IsPinned = item.PackageReferenceEntries.All(entry => entry.IsPinned) })
            .ToArray();

        yield return topLevelPackages.CountedDescription("update", item => item.PackageInfo.IsOutdated && !item.IsPinned);
        yield return topLevelPackages.CountedDescription("deprecation", item => item.PackageInfo.IsDeprecated && !item.IsPinned);
        yield return topLevelPackages.CountedDescription("vulnerability", item => item.PackageInfo.IsVulnerable && item.PackageInfo.VulnerabilityMitigation.IsNullOrEmpty());
    }
}