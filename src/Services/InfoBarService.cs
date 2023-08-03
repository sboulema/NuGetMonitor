using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Models;

namespace NuGetMonitor.Services;

public static class InfoBarService
{
    private static InfoBar? _infoBar { get; set; }

    public static async Task ShowInfoBar(IReadOnlyCollection<PackageInfo> packageReferences)
    {
        var outdatedCount = packageReferences.Count(packageReference => packageReference.IsOutdated);
        var deprecatedCount = packageReferences.Count(packageReference => packageReference.IsDeprecated);
        var vulnerableCount = packageReferences.Count(packageReference => packageReference.IsVulnerable);

        if (outdatedCount == 0 &&
            deprecatedCount == 0 &&
            vulnerableCount == 0)
        {
            return;
        }

        var model = new InfoBarModel(
            GetTextSpans(outdatedCount, deprecatedCount, vulnerableCount),
            KnownMonikers.NuGet,
            isCloseButtonVisible: true);

        _infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model).ConfigureAwait(true) ?? throw new InvalidOperationException("Failed to create the info bar");
        _infoBar.ActionItemClicked += InfoBar_ActionItemClicked;

        await _infoBar.TryShowInfoBarUIAsync().ConfigureAwait(true);
    }

    public static void CloseInfoBar()
        => _infoBar?.Close();

    private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (e.ActionItem.Text == "Manage")
        {
            VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
        }

        (sender as InfoBar)?.Close();
    }

    private static List<IVsInfoBarTextSpan> GetTextSpans(int outdatedCount, int deprecatedCount, int vulnerableCount)
    {
        // Idea for showing counts, not sure if unicode icons in a InfoBar feel native
        // new InfoBarTextSpan($"NuGet update: 🔼 {outdatedCount} ⚠ {deprecatedCount} 💀 {vulnerableCount}. "),

        var textSpans = new List<IVsInfoBarTextSpan>();

        if (outdatedCount > 0)
        {
            textSpans.Add(new InfoBarTextSpan($"{outdatedCount} {(outdatedCount == 1 ? "update" : "updates")}"));
        }

        if (deprecatedCount > 0)
        {
            textSpans.Add(new InfoBarTextSpan($"{(textSpans.Any() ? ", " : string.Empty)}{deprecatedCount} {(deprecatedCount == 1 ? "deprecation" : "deprecations")}"));
        }

        if (vulnerableCount > 0)
        {
            textSpans.Add(new InfoBarTextSpan($"{(textSpans.Any() ? ", " : string.Empty)}{vulnerableCount} {(vulnerableCount == 1 ? "vulnerability" : "vulnerabilities")}"));
        }

        textSpans.Add(new InfoBarTextSpan(". "));
        textSpans.Add(new InfoBarHyperlink("Manage"));
        textSpans.Add(new InfoBarTextSpan(" packages."));

        return textSpans;
    }
}