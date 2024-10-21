namespace NuGetMonitor.Model.Models;

public sealed record PackageReferenceInfo(PackageInfo PackageInfo, HashSet<PackageReferenceEntry> PackageReferenceEntries);