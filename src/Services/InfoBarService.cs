using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;
using NuGetMonitor.Models;
using System.Collections.Generic;
using System.Linq;

namespace NuGetMonitor.Services
{
    public class InfoBarService
    {
        public static async Task ShowInfoBar(IEnumerable<PackageReference> packageReferences)
        {
            var outdatedCount = packageReferences.Count(packageRefence => packageRefence.IsOutdated);
            var deprecatedCount = packageReferences.Count(packageRefence => packageRefence.IsDeprecated);
            var vulnerableCount = packageReferences.Count(packageRefence => packageRefence.IsVulnerable);

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

            var infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model);
            infoBar.ActionItemClicked += InfoBar_ActionItemClicked;

            await infoBar.TryShowInfoBarUIAsync();
        }

        private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.ActionItem.Text == "Manage")
            {
                VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
            }

            (sender as InfoBar).Close();
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
}
