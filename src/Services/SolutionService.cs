using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using NuGetMonitor.Abstractions;
using NuGetMonitor.View.Monitor;
using TomsToolbox.Essentials;

namespace NuGetMonitor.Services;

internal sealed class SolutionService : ISolutionService
{
    private SolutionService()
    {
        VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;
        VS.Events.SolutionEvents.OnAfterCloseSolution += SolutionEvents_OnAfterCloseSolution;
    }

    public static readonly ISolutionService Instance = new SolutionService();

    private void SolutionEvents_OnAfterCloseSolution()
    {
        SolutionClosed?.Invoke(this, EventArgs.Empty);
    }

    private void SolutionEvents_OnAfterOpenSolution(Solution? obj)
    {
        SolutionOpened?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> GetSolutionFolder()
    {
        var solution = await VS.Solutions.GetCurrentSolutionAsync();

        return solution?.FullPath;
    }

    public async Task<ICollection<string>> GetProjectFilePaths()
    {
        var projects = await VS.Solutions.GetAllProjectsAsync();

        var filePaths = projects.Select(project => project.FullPath)
            .ExceptNullItems()
            .ToArray();

        return filePaths;
    }

    public event EventHandler? SolutionOpened;

    public event EventHandler? SolutionClosed;

    public void ShowPackageManager()
    {
        VS.Commands.ExecuteAsync("Tools.ManageNuGetPackagesForSolution").FireAndForget();
    }

    public async Task ShowInfoBar(string message)
    {
        var model = new InfoBarModel(message);
        var infoBar = await VS.InfoBar.CreateAsync(NuGetMonitorToolWindow.Id, model).ConfigureAwait(true) ?? throw new InvalidOperationException("Failed to create the info bar");
        await infoBar.TryShowInfoBarUIAsync().ConfigureAwait(true);

        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        infoBar.Close();
    }

    public void OpenDocument(string path)
    {
        VS.Documents.OpenAsync(path).FireAndForget();
    }
}