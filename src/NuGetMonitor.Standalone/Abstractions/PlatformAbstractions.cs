using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Microsoft.Build.Construction;
using NuGetMonitor.Services;

namespace NuGetMonitor.Abstractions;

internal static class PlatformAbstractions
{
    public static string? SolutionPath { get; private set; }

    public static void OpenSolution(string? solutionFilePath)
    {
        if (SolutionPath is not null)
        {
            SolutionClosed?.Invoke(null, EventArgs.Empty);
        }

        SolutionPath = solutionFilePath;

        if (solutionFilePath is not null)
        {
            SolutionOpened?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void OpenDocument(string path)
    {
        // In standalone mode, we could open files with the default application
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors if file cannot be opened
        }
    }

    public static async Task<ICollection<string>> GetProjectFilePaths()
    {
        if (string.IsNullOrEmpty(SolutionPath) || !File.Exists(SolutionPath))
            return [];

        return await Task.Run(ICollection<string> () =>
        {
            try
            {
                var solutionFile = SolutionFile.Parse(SolutionPath);

                var solutionDirectory = Path.GetDirectoryName(SolutionPath) ?? ".";

                var projectPaths = solutionFile.ProjectsInOrder
                    .Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
                    .Select(p => Path.GetFullPath(Path.Combine(solutionDirectory, p.RelativePath)))
                    .Where(File.Exists)
                    .ToArray();

                return projectPaths;
            }
            catch
            {
                return [];
            }
        });
    }

    public static event EventHandler? SolutionOpened;

    public static event EventHandler? SolutionClosed;

    public static async Task ShowInfoBar(string message)
    {
        InfoBarService.Instance.ShowMessage(message);

        await Task.CompletedTask;
    }

    public static void FireAndForget(this System.Threading.Tasks.Task task, bool logOnFailure = true)
    {
        task.ContinueWith(delegate { }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default).Forget();
    }

    public static void Forget(this Task? task)
    {
    }

    public static async Task<bool> ShowNoYesMessageBox(string line1, string line2)
    {
        var taskCompletionSource = new TaskCompletionSource<bool>();

        var noButton = new Button { Content = "No", IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var yesButton = new Button { Content = "Yes", IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

        var dialog = new Window
        {
            Title = "NuGet Monitor",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            MinWidth = 320,
            MaxWidth = 640,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = line1, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = line2, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { noButton, yesButton }
                    }
                }
            }
        };

        noButton.Click += (_, _) =>
        {
            taskCompletionSource.TrySetResult(false);
            dialog.Close();
        };

        yesButton.Click += (_, _) =>
        {
            taskCompletionSource.TrySetResult(true);
            dialog.Close();
        };

        dialog.Closed += (_, _) => taskCompletionSource.TrySetResult(false);

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (owner is not null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        return await taskCompletionSource.Task;
    }
}
