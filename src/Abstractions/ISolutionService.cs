namespace NuGetMonitor.Model.Abstractions
{
    public interface ISolutionService
    {
        Task<string?> GetSolutionFolder();

        Task<ICollection<string>> GetProjectFolders();

        event EventHandler SolutionOpened;

        event EventHandler SolutionClosed;

        void ShowPackageManager();

        Task ShowInfoBar(string message);

        void OpenDocument(string path);
    }
}