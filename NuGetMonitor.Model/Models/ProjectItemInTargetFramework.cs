using Microsoft.Build.Evaluation;

namespace NuGetMonitor.Model.Models;

public sealed record ProjectItemInTargetFramework(ProjectItem ProjectItem, ProjectInTargetFramework Project);
