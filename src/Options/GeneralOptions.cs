using System.ComponentModel;
using System.Runtime.InteropServices;
using Community.VisualStudio.Toolkit;

namespace NuGetMonitor.Options;

[ComVisible(true)]
public class GeneralOptionsPage : BaseOptionPage<GeneralOptions> { }

public sealed class GeneralOptions : BaseOptionModel<GeneralOptions>
{
    [Category("Notifications")]
    [DisplayName("Show transitive packages issues")]
    [Description("Show a notification detailing vulnerable transitive packages.")]
    [DefaultValue(true)]
    public bool ShowTransitivePackagesIssues { get; set; } = true;
}

