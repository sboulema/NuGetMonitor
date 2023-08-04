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
    internal partial class PackageViewModel : INotifyPropertyChanged
    {
        private readonly NugetMonitorViewModel _owner;

        public PackageViewModel(IGrouping<PackageIdentity, PackageReferenceEntry> items, NugetMonitorViewModel owner)
        {
            _owner = owner;
            Items = items;
            Identity = items.Key;
            ProjectPaths = string.Join(", ", items.Select(item => item.RelativePath));
        }

        public IGrouping<PackageIdentity, PackageReferenceEntry> Items { get; }

        [OnChangedMethod(nameof(OnIdentityChanged))]
        public PackageIdentity Identity { get; private set; }

        public string ProjectPaths { get; }

        public Package? Package { get; private set; }

        public NuGetVersion? SelectedVersion { get; set; }

        public bool IsUpdateAvailable => SelectedVersion != Identity.Version;

        public bool IsUpToDate => !IsUpdateAvailable;

        public bool IsLoading => Package == null;

        public bool IsSelected { get; set; }

        public ICommand Update => new DelegateCommand(() => { _owner.Update(this); });

        public PackageInfo? PackageInfo { get; private set; }

        public async Task Load()
        {
            Package = await NuGetService.GetPackage(Identity.Id).ConfigureAwait(false);
            SelectedVersion = Package.Versions.FirstOrDefault(i => !i.IsPrerelease && i >= Identity.Version) ?? Package.Versions.FirstOrDefault();
            IsSelected = IsUpdateAvailable;
        }

        public void ApplyVersion()
        {
            Identity = new PackageIdentity(Identity.Id, SelectedVersion);
        }

        private async void OnIdentityChanged()
        {
            PackageInfo = await NuGetService.GetPackageInfo(Identity).ConfigureAwait(false);
        }
    }
}