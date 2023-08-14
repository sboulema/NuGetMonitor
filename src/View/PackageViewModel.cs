using System.ComponentModel;
using System.Windows.Input;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Models;
using NuGetMonitor.Services;
using PropertyChanged;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View
{
    internal sealed partial class PackageViewModel : INotifyPropertyChanged
    {
        public PackageViewModel(IGrouping<PackageIdentity, PackageReferenceEntry> items)
        {
            Items = items;
            Identity = items.Key;
            Projects = items.GroupBy(item => item.ProjectItem.Xml.ContainingProject).Select(item => new ProjectViewModel(item.Key)).ToArray();
        }

        public IGrouping<PackageIdentity, PackageReferenceEntry> Items { get; }

        public ICollection<ProjectViewModel> Projects { get; }

        [OnChangedMethod(nameof(OnIdentityChanged))]
        public PackageIdentity Identity { get; private set; }

        public Package? Package { get; private set; }

        public NuGetVersion? SelectedVersion { get; set; }

        public bool IsUpdateAvailable => SelectedVersion is not null && SelectedVersion != Identity.Version;

        public bool IsLoading => Package == null;

        public ICommand UpdateCommand => new DelegateCommand(() => IsUpdateAvailable, () => { NuGetMonitorViewModel.Update(this); });

        public PackageInfo? PackageInfo { get; private set; }

        public async Task Load()
        {
            try
            {
                Package = await NuGetService.GetPackage(Identity.Id).ConfigureAwait(false);

                var versions = Package?.Versions ?? Array.Empty<NuGetVersion>();

                SelectedVersion = versions.FirstOrDefault(i => !i.IsPrerelease && i >= Identity.Version) ?? versions.FirstOrDefault();
            }
            catch (OperationCanceledException)
            {
                // session cancelled
            }
        }

        public void ApplyVersion()
        {
            Identity = new PackageIdentity(Identity.Id, SelectedVersion);
        }

        private async void OnIdentityChanged()
        {
            try
            {
                PackageInfo = await NuGetService.GetPackageInfo(Identity).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // session cancelled
            }
        }
    }
}