using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;

namespace NuGetMonitor.Services
{
    public class MonitorService
    {
        public static void RegisterEventHandler()
        {
            VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        }

        private static void SolutionEvents_OnAfterOpenSolution(Solution solution)
        {
            CheckForUpdates().FireAndForget();
        }

        public static async Task CheckForUpdates()
        {
            var packageReferences = await ProjectService.GetPackageReferences();

            packageReferences = await NuGetService.CheckPackageReferences(packageReferences);

            await InfoBarService.ShowInfoBar(packageReferences);
        }
    }
}
