using Community.VisualStudio.Toolkit;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NuGetMonitor.Options;

internal sealed class OptionsProvider
{
    [ComVisible(true)]
    public class GeneralOptions : BaseOptionPage<General> { }
}

public sealed class General : BaseOptionModel<General>
{
    [Category("Notifications")]
    [DisplayName("Show transitive packages issues")]
    [Description("Show a notification detailing vulnerable transitive packages.")]
    [DefaultValue(true)]
    public bool ShowTransitivePackagesIssues { get; set; } = true;
}

