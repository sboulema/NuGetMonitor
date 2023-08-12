using System.ComponentModel;
using System.Windows.Input;
using Community.VisualStudio.Toolkit;
using Microsoft.Build.Evaluation;
using Microsoft.IO;
using Microsoft.VisualStudio.Shell;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View
{
    internal partial class ProjectViewModel : INotifyPropertyChanged
    {
        private readonly ProjectItem _projectItem;

        public ProjectViewModel(ProjectItem projectItem)
        {
            _projectItem = projectItem;
        }

        public string Name => Path.GetFileName(_projectItem.Project.FullPath);

        public ICommand OpenProjectCommand => new DelegateCommand(OpenProject);

        private void OpenProject()
        {
            var document = Keyboard.Modifiers == ModifierKeys.Control
                ? _projectItem.Xml.ContainingProject.FullPath
                : _projectItem.Project.FullPath;

            VS.Documents.OpenAsync(document).FireAndForget();
        }
    }
}