using System.ComponentModel;
using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;

namespace NuGetMonitor.Options;

[ComVisible(true)]
public class GeneralOptionsPage : BaseOptionPage<GeneralOptions> { }

public sealed class GeneralOptions : BaseOptionModel<GeneralOptions>
{
    [Category("Notifications")]
    [DisplayName("Close notification after clicking")]
    [Description("Automatically close the notification after clicking on an action item.")]
    [DefaultValue(true)]
    public bool CloseInfoBar { get; set; } = true;

    [Category("Notifications")]
    [DisplayName("Show transitive packages issues")]
    [Description("Show a notification detailing vulnerable transitive packages.")]
    [DefaultValue(true)]
    public bool ShowTransitivePackagesIssues { get; set; } = true;

    [Category("Notifications")]
    [DisplayName("Open NuGet Package Manager")]
    [Description("Open the built-in NuGet Package Manager instead of the NuGet Monitor Package Manager.")]
    [DefaultValue(true)]
    public bool OpenNuGetPackageManager { get; set; } = false;

    [Category("Notifications")]
    [DisplayName("Log redundant package references")]
    [Description("Log all potentially redundant package references where the package is already introduced by a referenced project")]
    [DefaultValue(true)]
    public bool LogRedundantPackageReferences { get; set; } = true;
}

