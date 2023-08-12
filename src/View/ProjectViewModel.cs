using System.ComponentModel;
using System.Windows.Input;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Construction;
using Microsoft.IO;
using Microsoft.VisualStudio.Shell;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View
{
    internal partial class ProjectViewModel : INotifyPropertyChanged
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
            VS.Documents.OpenAsync(_project.FullPath).FireAndForget();
        }
    }
}