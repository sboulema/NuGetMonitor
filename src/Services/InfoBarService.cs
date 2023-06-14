using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;

namespace NuGetMonitor.Services
{
    public class InfoBarService
    {
        public static async Task ShowInfoBar()
        {
            var model = new InfoBarModel(
                new[] {
                    new InfoBarTextSpan("NuGet updates available. "),
                    new InfoBarHyperlink("Manage NuGet")
                },
                KnownMonikers.NuGet,
                true);

            var infoBar = await VS.InfoBar.CreateAsync(ToolWindowGuids80.SolutionExplorer, model);
            infoBar.ActionItemClicked += InfoBar_ActionItemClicked;

            await infoBar.TryShowInfoBarUIAsync();
        }

        private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.ActionItem.Text == "Manage NuGet")
            {
                VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
            }
        }
    }
}
