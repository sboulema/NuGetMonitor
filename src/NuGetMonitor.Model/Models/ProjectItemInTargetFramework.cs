using System.Diagnostics;
using Microsoft.Build.Evaluation;

namespace NuGetMonitor.Model.Models;

[DebuggerDisplay("{ProjectItem}, {Project}")]
public sealed record ProjectItemInTargetFramework(ProjectItem ProjectItem, ProjectInTargetFramework Project);
