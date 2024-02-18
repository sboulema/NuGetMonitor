using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using NuGet.Versioning;
using NuGetMonitor.Model.Abstractions;
using NuGetMonitor.Models;
using NuGetMonitor.Services;
using PropertyChanged;
using TomsToolbox.Wpf;

namespace NuGetMonitor.View
{
    internal sealed partial class PackageViewModel : INotifyPropertyChanged
    {
        public PackageViewModel(IGrouping<PackageReference, PackageReferenceEntry> items, ISolutionService solutionService)
        {
            Items = items;
            PackageReference = items.Key;
            Projects = items.GroupBy(item => item.ProjectItemInTargetFramework.ProjectItem.GetContainingProject()).Select(item => new ProjectViewModel(item.Key, solutionService)).ToArray();
            ActiveVersion = NuGetVersion.TryParse(PackageReference.VersionRange.OriginalString, out var simpleVersion) ? simpleVersion : PackageReference.VersionRange;
            Justifications = string.Join(", ", Items.Select(reference => reference.Justification).Distinct());
        }

        public IGrouping<PackageReference, PackageReferenceEntry> Items { get; }

        public ICollection<ProjectViewModel> Projects { get; }

        [OnChangedMethod(nameof(OnPackageReferenceChanged))]
        public PackageReference PackageReference { get; private set; }

        public object? ActiveVersion { get; private set; }

        public Package? Package { get; private set; }

        [OnChangedMethod(nameof(OnSelectedVersionChanged))]
        public NuGetVersion? SelectedVersion { get; set; }

        public bool IsUpdateAvailable { get; private set; }

        public bool IsLoading => Package == null;

        public ICommand UpdateCommand => new DelegateCommand(() => IsUpdateAvailable, () => { NuGetMonitorViewModel.Update(this); });

        // ! ProjectUrl is checked in CanExecute
        public ICommand OpenProjectUrlCommand => new DelegateCommand(() => PackageInfo?.ProjectUrl != null, () => Process.Start(PackageInfo!.ProjectUrl.AbsoluteUri));

        public PackageInfo? PackageInfo { get; private set; }

        public string Justifications { get; }

        public async Task Load()
        {
            try
            {
                Package = await NuGetService.GetPackage(PackageReference.Id);

                var versions = Package?.Versions ?? Array.Empty<NuGetVersion>();

                SelectedVersion = ActiveVersion switch
                {
                    NuGetVersion version => versions.FirstOrDefault(i => !i.IsPrerelease && i >= version) ?? versions.FirstOrDefault(),
                    VersionRange versionRange => versionRange.FindBestMatch(versions),
                    _ => null
                };
            }
            catch (OperationCanceledException)
            {
                // session cancelled
            }
        }

        public void ApplySelectedVersion()
        {
            if (SelectedVersion is null)
                return;

            PackageReference = PackageReference with { VersionRange = new VersionRange(SelectedVersion) };
            IsUpdateAvailable = false;
            ActiveVersion = SelectedVersion;
        }

        private void OnSelectedVersionChanged()
        {
            IsUpdateAvailable = (SelectedVersion is not null) && ActiveVersion switch
            {
                NuGetVersion version => version != SelectedVersion,
                VersionRange versionRange => versionRange.FindBestMatch(Package?.Versions) != SelectedVersion,
                _ => false
            };
        }

        private async void OnPackageReferenceChanged()
        {
            try
            {
                Package = await NuGetService.GetPackage(PackageReference.Id);

                var packageIdentity = PackageReference.FindBestMatch(Package?.Versions);

                if (packageIdentity != null)
                {
                    PackageInfo = await NuGetService.GetPackageInfo(packageIdentity);
                }
            }
            catch (OperationCanceledException)
            {
                // session cancelled
            }
        }
    }
}