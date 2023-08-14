using System.Text;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Models;
using NuGetMonitor.View;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

internal static class InfoBarService
{
    private static readonly List<InfoBar> _infoBars = new();

    private enum Actions
    {
        Manage
    }

    public static void ShowTopLevelPackageIssues(ICollection<PackageInfo> topLevelPackages)
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

        ShowInfoBar(textSpans).FireAndForget();
    }

    public static void ShowTransitivePackageIssues(IEnumerable<PackageInfo> transitivePackages, ICollection<PackageInfo> topLevelPackages)
    {
        var vulnerablePackages = transitivePackages.Where(item => item.IsVulnerable).ToArray();

        if (vulnerablePackages.Length <= 0)
        {
            if (topLevelPackages.Count > 0)
                ShowInfoBar("No vulnerabilities in transient packages found");

            return;
        }

        var packageInfo = string.Join("\r\n- ", vulnerablePackages.Select(package => package.PackageIdentity));
        var text = $"{CountedDescription(vulnerablePackages, "vulnerability")} in transitive dependencies:\r\n- {packageInfo}\r\n";

        var textSpans = new[]
        {
            new InfoBarTextSpan(text),
            new InfoBarHyperlink("Copy details", vulnerablePackages)
            // TODO: maybe show the "Manage" link, when the UI can show some details about this?
            // or show the individual vulnerability hyperlinks to open the link in a browser?
        };

        ShowInfoBar(textSpans).FireAndForget();
    }

    private static void ShowInfoBar(string text, TimeSpan? timeOut = default)
    {
        ShowInfoBar(new[] { new InfoBarTextSpan(text) }, timeOut).FireAndForget();
    }

    private static async Task ShowInfoBar(IEnumerable<InfoBarTextSpan> textSpans, TimeSpan? timeOut = default)
    {
        var model = new InfoBarModel(textSpans, KnownMonikers.NuGet, isCloseButtonVisible: true);

        var infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model).ConfigureAwait(true) ?? throw new InvalidOperationException("Failed to create the info bar");
        infoBar.ActionItemClicked += InfoBar_ActionItemClicked;

        _infoBars.Add(infoBar);

        await infoBar.TryShowInfoBarUIAsync().ConfigureAwait(true);

        if (timeOut.HasValue)
        {
            await Task.Delay(timeOut.Value).ConfigureAwait(true);
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
        ThreadHelper.ThrowIfNotOnUIThread();

        switch (e.ActionItem.ActionContext)
        {
            case Actions.Manage:
                NuGetMonitorCommand.Instance?.ShowToolWindow();
                break;
            case ICollection<PackageInfo> packages:
                PrintDependencyTree(packages);
                break;
        }

        (sender as InfoBar)?.Close();
    }

    private static void PrintDependencyTree(IEnumerable<PackageInfo> packages)
    {
        var text = new StringBuilder();

        foreach (var package in packages)
        {
            PrintDependencyTree(text, package, 0);
        }

        Clipboard.SetText(text.ToString());

        ShowInfoBar("Dependency tree copied to clipboard", TimeSpan.FromSeconds(10));
    }

    private static void PrintDependencyTree(StringBuilder text, PackageInfo package, int nesting)
    {
        var indent = new string(' ', nesting * 4);

        text.Append(indent);
        text.Append(package.PackageIdentity);

        if (package.IsDeprecated)
            text.Append(" - Deprecated");

        if (package.IsOutdated)
            text.Append(" - Outdated");

        if (package.Vulnerabilities?.Count > 0)
        {
            text.Append(" - Vulnerable:");
            foreach (var item in package.Vulnerabilities)
            {
                text.Append($" [ Severity: {item.Severity}, {item.AdvisoryUrl} ]");
            }
        }

        text.AppendLine();


        foreach (var info in package.DependsOn)
        {
            PrintDependencyTree(text, info, nesting + 1);
        }
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