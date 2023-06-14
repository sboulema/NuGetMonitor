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

        private static async Task CheckForUpdates()
        {
            var hasUpdates = await HasUpdates();

            if (!hasUpdates)
            {
                return;
            }

            await InfoBarService.ShowInfoBar();
        }

        private static async Task<bool> HasUpdates()
        {
            var projectPaths = await ProjectService.GetProjectPaths();

            foreach (var path in projectPaths)
            {
                var packageReferences = ProjectService.GetPackageReferences(path);

                foreach (var packageReference in packageReferences)
                {
                    var latestVersion = await NuGetService.GetLatestVersion(packageReference.Include);

                    if (latestVersion > packageReference.Version)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
