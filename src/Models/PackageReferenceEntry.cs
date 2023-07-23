using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;

namespace NuGetMonitor.Models
{
    public record PackageReferenceEntry(PackageIdentity Identity, ProjectItem ProjectItem);
}