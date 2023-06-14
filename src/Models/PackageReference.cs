using System;

namespace NuGetMonitor.Models
{
    public class PackageReference
    {
        public string Include { get; set; } = string.Empty;

        public Version Version { get; set; }
    }
}
