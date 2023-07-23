using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Tests")]

namespace System.Runtime.CompilerServices
{
    [ExcludeFromCodeCoverage]
    [DebuggerNonUserCode]
    internal static class IsExternalInit
    {
    }
}