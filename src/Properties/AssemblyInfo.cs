using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Shell;

[assembly: InternalsVisibleTo("Tests")]

[assembly: ProvideCodeBase(AssemblyName = "TomsToolbox.Essentials")]
[assembly: ProvideCodeBase(AssemblyName = "TomsToolbox.Wpf")]

namespace System.Runtime.CompilerServices;

[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
internal static class IsExternalInit
{
}