﻿using System.IO;
using System.Text;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Models;
using NuGetMonitor.View;
using TomsToolbox.Essentials;

using static NuGetMonitor.Services.LoggingService;


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
        var transitivePackages = transitiveDependencies
            .SelectMany(project => project.Packages.Keys)
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
        var message = $"{CountedDescription(vulnerablePackages, "vulnerability")} in transitive dependencies:\r\n- {packageInfo}\r\n";

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
            var (project, targetFramework, packages) = dependency;

            var vulnerablePackages = packages
                .Select(item => item.Key)
                .Where(item => item.IsVulnerable)
                .ToArray();

            if (vulnerablePackages.Length == 0)
                continue;

            var header = $"{Path.GetFileName(project.FullPath)}, {targetFramework}";

            text.AppendLine(header)
                .AppendLine(new string('-', header.Length));

            foreach (var vulnerablePackage in vulnerablePackages)
            {
                PrintDependencyTree(text, vulnerablePackage, dependency.Packages, 0);
            }

            text.AppendLine().AppendLine();
        }

        Clipboard.SetText(text.ToString());

        ShowInfoBar("Dependency tree copied to clipboard", TimeSpan.FromSeconds(10));
    }

    private static void PrintDependencyTree(StringBuilder text, PackageInfo package, IReadOnlyDictionary<PackageInfo, HashSet<PackageInfo>> dependencyTree, int nesting)
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

        if (!dependencyTree.TryGetValue(package, out var dependsOn))
            return;

        foreach (var item in dependsOn)
        {
            PrintDependencyTree(text, item, dependencyTree, nesting + 1);
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