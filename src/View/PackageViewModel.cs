using System.ComponentModel;
using System.Windows.Input;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Models;
using NuGetMonitor.Services;
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

        public PackageIdentity Identity { get; private set; }

        public string ProjectPaths { get; }

        public async Task Load()
        {
            Package = await NuGetService.GetPackage(Identity.Id).ConfigureAwait(false);
            SelectedVersion = Package.Versions.FirstOrDefault(i => !i.IsPrerelease && i >= Identity.Version) ?? Package.Versions.FirstOrDefault();
            IsSelected = IsUpdateAvailable;
        }

        public Package? Package { get; private set; }

        public NuGetVersion? SelectedVersion { get; set; }

        public bool IsUpdateAvailable => SelectedVersion != Identity.Version;

        public bool IsSelected { get; set; }

        public ICommand Update => new DelegateCommand(() => { _owner.Update(this); });

        public void ApplyVersion()
        {
            Identity = new PackageIdentity(Identity.Id, SelectedVersion);
        }
    }
}