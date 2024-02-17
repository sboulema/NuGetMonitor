using System.Text;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Versioning;
using NuGetMonitor.Model;
using NuGetMonitor.Models;
using NuGetMonitor.Options;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

internal static class InfoBarService
{
    private static readonly List<InfoBar> _infoBars = new();

    private enum Actions
    {
        Manage
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
            .SelectMany(dependency => dependency.ParentsByChild.Keys)
            .Distinct()
            .ToArray();

        Log($"{transitivePackages.Length} transitive packages found");

        var vulnerablePackages = transitivePackages.Where(item => item.IsVulnerable).ToArray();

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
            new InfoBarHyperlink("Copy details", transitiveDependencies)
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
                NuGetMonitorCommands.Instance?.ShowMonitorToolWindow();
                break;

            case ICollection<TransitiveDependencies> transitiveDependencies:
                PrintDependencyTree(transitiveDependencies);
                break;
        }

        (sender as InfoBar)?.Close();
    }

    private static void PrintDependencyTree(IEnumerable<TransitiveDependencies> dependencies)
    {
        var text = new StringBuilder();

        foreach (var dependency in dependencies)
        {
            var (projectName, _, targetFramework, packages) = dependency;

            var vulnerablePackages = packages
                .Select(item => item.Key)
                .Where(item => item.IsVulnerable)
                .ToArray();

            if (vulnerablePackages.Length == 0)
                continue;

            var header = $"{projectName}, {targetFramework}";

            text.AppendLine(header)
                .AppendLine(new string('-', header.Length));

            foreach (var vulnerablePackage in vulnerablePackages)
            {
                PrintDependencyTree(text, vulnerablePackage, packages, 0);
            }

            text.AppendLine().AppendLine();
        }

        Clipboard.SetText(text.ToString());

        ShowInfoBar("Dependency tree copied to clipboard", TimeSpan.FromSeconds(10));
    }

    private static void PrintDependencyTree(StringBuilder text, PackageInfo package, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> parentsByChild, int nesting)
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

        if (!parentsByChild.TryGetValue(package, out var dependsOn))
            return;

        foreach (var item in dependsOn)
        {
            PrintDependencyTree(text, item, parentsByChild, nesting + 1);
        }
    }

    private static IEnumerable<string?> GetInfoTexts(IEnumerable<PackageReferenceInfo> topLevelPackageInfos)
    {
        // Idea for showing counts, not sure if unicode icons in a InfoBar feel native
        // new InfoBarTextSpan($"NuGet update: 🔼 {outdatedCount} ⚠ {deprecatedCount} 💀 {vulnerableCount}. "),

        var topLevelPackages = topLevelPackageInfos
            .Where(item => item.PackageReferenceEntries.Any(entry => NuGetVersion.TryParse(entry.Identity.VersionRange.OriginalString, out _)))
            .Select(item => item.PackageInfo)
            .ToArray();

        yield return topLevelPackages.CountedDescription("update", item => item.IsOutdated);
        yield return topLevelPackages.CountedDescription("deprecation", item => item.IsDeprecated);
        yield return topLevelPackages.CountedDescription("vulnerability", item => item.IsVulnerable);
    }
}