using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Build.Construction;
using NuGetMonitor.Abstractions;

namespace NuGetMonitor.ViewModels;

internal sealed partial class ProjectViewModel : INotifyPropertyChanged
{
    private readonly ProjectRootElement _project;
    public ProjectViewModel(ProjectRootElement project)
    {
        _project = project;
    }

    public string Name => Path.GetFileName(_project.FullPath);

    public ICommand OpenProjectCommand => new DelegateCommand(OpenProject);

    private void OpenProject()
    {
        PlatformAbstractions.OpenDocument(_project.FullPath);
    }
}