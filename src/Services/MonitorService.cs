using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;

namespace NuGetMonitor.Services
{
    public static class MonitorService
    {
        public static void RegisterEventHandler()
        {
            VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
            VS.Events.SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
        }

        private static void SolutionEvents_OnAfterCloseSolution()
            => InfoBarService.CloseInfoBar();

        private static void SolutionEvents_OnAfterOpenSolution(Solution solution)
            => CheckForUpdates().FireAndForget();

        public static async Task CheckForUpdates()
        {
            var packageIdentities = await ProjectService.GetPackageReferences().ConfigureAwait(true);

            var packageReferences = await NuGetService.CheckPackageReferences(packageIdentities).ConfigureAwait(true);

            await InfoBarService.ShowInfoBar(packageReferences).ConfigureAwait(true);
        }
    }
}
