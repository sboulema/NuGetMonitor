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
    private static readonly List<InfoBar> _infoBars = new();

    private enum Actions
    {
        Manage
    }

    public static async Task ShowTopLevelPackageIssues(ICollection<PackageInfo> topLevelPackages)
    {
        var infoText = string.Join(", ", GetInfoTexts(topLevelPackages).ExceptNullItems());
        if (string.IsNullOrEmpty(infoText))
            return;

        var textSpans = new[]
        {
            new InfoBarTextSpan($"{infoText}. "),
            new InfoBarHyperlink("Manage", Actions.Manage),
            new InfoBarTextSpan(" packages.")
        };

        await ShowInfoBar(textSpans).ConfigureAwait(false);
    }

    public static async Task ShowTransitivePackageIssues(IEnumerable<PackageInfo> transitivePackages)
    {
        var vulnerablePackages = transitivePackages.Where(item => item.IsVulnerable).ToArray();

        if (vulnerablePackages.Length <= 0) 
            return;

        var packageInfo = string.Join("\r\n- ", vulnerablePackages.Select(package => package.PackageIdentity));
        var text = $"{CountedDescription(vulnerablePackages, "vulnerability")} in transitive dependencies:\r\n- {packageInfo}\r\n";

        var textSpans = new[]
        {
            new InfoBarTextSpan(text)
            // TODO: maybe show the "Manage" link, when the UI can show some details about this?
            // or show the individual vulnerability hyperlinks to open the link in a browser?
        };

        await ShowInfoBar(textSpans).ConfigureAwait(false);
    }

    private static async Task ShowInfoBar(InfoBarTextSpan[] textSpans)
    {
        var model = new InfoBarModel(textSpans, KnownMonikers.NuGet, isCloseButtonVisible: true);

        var infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model).ConfigureAwait(true) ?? throw new InvalidOperationException("Failed to create the info bar");
        infoBar.ActionItemClicked += InfoBar_ActionItemClicked;
        infoBar.TryShowInfoBarUIAsync().FireAndForget();

        _infoBars.Add(infoBar);
    }

    public static void CloseInfoBars()
    {
        _infoBars.ForEach(item => item.Close());
        _infoBars.Clear();
    }

    private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (e.ActionItem.ActionContext is Actions.Manage)
        {
            NuGetMonitorCommand.Instance?.ShowToolWindow();
        }

        (sender as InfoBar)?.Close();
    }

    private static IEnumerable<string?> GetInfoTexts(ICollection<PackageInfo> topLevelPackages)
    {
        // Idea for showing counts, not sure if unicode icons in a InfoBar feel native
        // new InfoBarTextSpan($"NuGet update: 🔼 {outdatedCount} ⚠ {deprecatedCount} 💀 {vulnerableCount}. "),

        yield return CountedDescription(topLevelPackages, "update", item => item.IsOutdated);
        yield return CountedDescription(topLevelPackages, "deprecation", item => item.IsDeprecated);
        yield return CountedDescription(topLevelPackages, "vulnerability", item => item.IsVulnerable);
    }

    public static string? CountedDescription<T>(this IEnumerable<T> items, string singular, Func<T, bool>? selector = null)
    {
        selector ??= _ => true;

        var count = items.Count(selector);

        switch (count)
        {
            case <= 0:
                return null;
            case 1:
                return $"1 {singular}";
            default:
            {
                var plural = (singular.EndsWith("y", StringComparison.CurrentCulture)) ? singular.Substring(0, singular.Length - 1) + "ies" : singular + "s";

                return $"{count} {plural}";
            }
        }
    }
}