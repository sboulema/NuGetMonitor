using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Build.Construction;
using Microsoft.IO;
using NuGetMonitor.Model.Abstractions;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View
{
    internal sealed partial class ProjectViewModel : INotifyPropertyChanged
    {
        private readonly ProjectRootElement _project;
        private readonly ISolutionService _solutionService;

        public ProjectViewModel(ProjectRootElement project, ISolutionService solutionService)
        {
            _project = project;
            _solutionService = solutionService;
        }

        public string Name => Path.GetFileName(_project.FullPath);

        public ICommand OpenProjectCommand => new DelegateCommand(OpenProject);

        private void OpenProject()
        {
            _solutionService.OpenDocument(_project.FullPath);
        }
    }
}