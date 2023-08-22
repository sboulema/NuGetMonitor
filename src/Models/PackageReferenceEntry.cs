using Microsoft.Build.Evaluation;
using NuGet.Packaging.Core;

namespace NuGetMonitor.Models;

internal sealed record PackageReferenceEntry(PackageIdentity Identity, ProjectItem ProjectItem);