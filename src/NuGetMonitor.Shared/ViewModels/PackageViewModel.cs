using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGetMonitor.Model.Models;
using NuGetMonitor.Model.Services;
using NuGetMonitor.View.Monitor;
using PropertyChanged;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using Package = NuGetMonitor.Model.Models.Package;

namespace NuGetMonitor.ViewModels;

internal sealed partial class PackageViewModel : INotifyPropertyChanged
{
    private readonly NuGetMonitorViewModel _parent;

    public PackageViewModel(NuGetMonitorViewModel parent, IGrouping<PackageReference, PackageReferenceEntry> items, PackageItemType itemType)
    {
        _parent = parent;

        Items = items;
        PackageReference = items.Key;
        Projects = items.GroupBy(item => (itemType == PackageItemType.PackageVersion ? item.VersionSource : item.ProjectItemInTargetFramework.ProjectItem).GetContainingProject()).Select(item => new ProjectViewModel(item.Key)).ToArray();
        ActiveVersion = NuGetVersion.TryParse(PackageReference.VersionRange.OriginalString, out var simpleVersion) ? simpleVersion : PackageReference.VersionRange;
        Justifications = string.Join(", ", Items.Select(reference => reference.Justification).Distinct());
        IsPinned = items.Key.IsPinned;
        PinnedRange = items.Key.PinnedRange;
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

    public ICommand UpdateCommand => new DelegateCommand(() => IsUpdateAvailable, () => { _parent.Update(this); });

    // ! ProjectUrl is checked in CanExecute
    public ICommand OpenProjectUrlCommand => new DelegateCommand(() => PackageInfo?.ProjectUrl != null, () => OpenUrl(PackageInfo?.ProjectUrl?.AbsoluteUri));

    public ICommand OpenRepositoryUrlCommand => new DelegateCommand(() => PackageDetails?.RepositoryUrl != null, () => OpenUrl(PackageDetails?.RepositoryUrl));

    public PackageInfo? PackageInfo { get; private set; }

    public PackageDetails? PackageDetails { get; private set; }

    public string Justifications { get; }

    public bool IsPinned { get; }

    public VersionRange? PinnedRange { get; }

    public async Task LoadAsync()
    {
        try
        {
            Package = await NuGetService.GetPackage(PackageReference.Id);

            var versions = Package?.Versions ?? [];

            if (PackageReference is { PinnedRange: { } range })
            {
                versions = versions.Where(range.Satisfies).ToArray();
            }

            SelectedVersion = ActiveVersion switch
            {
                NuGetVersion version => versions.FirstOrDefault(i => !i.IsPrerelease && i >= version) ?? versions.FirstOrDefault(),
                VersionRange versionRange => versionRange.FindBestMatch(versions),
                _ => null
            };

            if (SelectedVersion is null)
                return;

            PackageDetails = await NuGetService.GetPackageDetails(new PackageIdentity(PackageReference.Id, SelectedVersion));
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

        PackageReference = PackageReference with { VersionRange = new(SelectedVersion) };
        IsUpdateAvailable = false;
        ActiveVersion = SelectedVersion;
    }

    private void OnSelectedVersionChanged()
    {
        IsUpdateAvailable = (!IsPinned || PinnedRange != null) && (SelectedVersion is not null) && ActiveVersion switch
        {
            NuGetVersion version => version != SelectedVersion,
            VersionRange versionRange => versionRange.FindBestMatch(Package?.Versions) != SelectedVersion,
            _ => false
        };
    }

    private void OnPackageReferenceChanged()
    {
        OnPackageReferenceChangedAsync().FireAndForget();
    }

    private async Task OnPackageReferenceChangedAsync()
    {
        try
        {
            Package = await NuGetService.GetPackage(PackageReference.Id);

            var packageIdentity = PackageReference.FindBestMatch(Package?.Versions);

            PackageInfo = await NuGetService.GetPackageInfo(packageIdentity);
        }
        catch (OperationCanceledException)
        {
            // session cancelled
        }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Failed to open URL {url}: {ex.Message}");
        }
    }
}