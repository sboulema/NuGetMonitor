using Microsoft.Build.Evaluation;

namespace NuGetMonitor.Model.Models;

public sealed class ProjectItemInTargetFramework
{
    public ProjectItemInTargetFramework(ProjectItem projectItem, ProjectInTargetFramework project)
    {
        ProjectItem = projectItem;
        Project = project;
    }

    public ProjectItem ProjectItem { get; init; }

    public ProjectInTargetFramework Project { get; }
}