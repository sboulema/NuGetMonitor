namespace NuGetMonitor.Models;

internal sealed record PackageReferenceInfo(PackageInfo PackageInfo, HashSet<PackageReferenceEntry> PackageReferenceEntries);