using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Models;
using NuGetMonitor.View;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

public static class InfoBarService
{
    private static InfoBar? _infoBar { get; set; }

    private enum Actions
    {
        Manage
    }

    public static async Task ShowInfoBar(IReadOnlyCollection<PackageInfo> topLevelPackages)
    {
        var infoTexts = string.Join(", ", GetInfoTexts(topLevelPackages).ExceptNullItems());
        if (string.IsNullOrEmpty(infoTexts))
            return;

        var textSpans = new[]
        {
            new InfoBarTextSpan(infoTexts),
            new InfoBarTextSpan(". "),
            new InfoBarHyperlink("Manage", Actions.Manage),
            new InfoBarTextSpan(" packages.")
        };

        var model = new InfoBarModel(textSpans, KnownMonikers.NuGet, isCloseButtonVisible: true);

        _infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model).ConfigureAwait(true) ?? throw new InvalidOperationException("Failed to create the info bar");
        _infoBar.ActionItemClicked += InfoBar_ActionItemClicked;
        _infoBar.TryShowInfoBarUIAsync().FireAndForget();
    }

    public static void CloseInfoBar() => _infoBar?.Close();

    private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (e.ActionItem.ActionContext is Actions.Manage)
        {
            NuGetMonitorCommand.Instance?.ShowToolWindow();
        }

        (sender as InfoBar)?.Close();
    }

    private static IEnumerable<string?> GetInfoTexts(IReadOnlyCollection<PackageInfo> topLevelPackages)
    {
        // Idea for showing counts, not sure if unicode icons in a InfoBar feel native
        // new InfoBarTextSpan($"NuGet update: 🔼 {outdatedCount} ⚠ {deprecatedCount} 💀 {vulnerableCount}. "),

        yield return CountedDescription(topLevelPackages, "update", item => item.IsOutdated);
        yield return CountedDescription(topLevelPackages,"deprecation", item => item.IsDeprecated);
        yield return CountedDescription(topLevelPackages, "vulnerability", item => item.IsVulnerable);
    }

    public static string? CountedDescription<T>(this IEnumerable<T> items, string singular, Func<T, bool> selector)
    {
        var count = items.Count(selector);

        switch (count)
        {
            case <= 0:
                return null;
            case 1:
                return $"1 {singular}";
            default:
            {
                var plural = (singular.EndsWith("y")) ? singular.Substring(0, singular.Length - 1) + "ies" : singular + "s";

                return $"{count} {plural}";
            }
        }
    }
}