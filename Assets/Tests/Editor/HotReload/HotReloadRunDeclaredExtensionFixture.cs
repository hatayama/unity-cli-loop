using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Internal extension class in the file a test passes, in a namespace that encloses the one of
    /// <c>HotReloadUnpassedSextupleExtensions</c>, so the target assembly lists its methods first.
    /// No other test calls Sextuple, so neither class is a candidate for another test's call.
    /// </summary>
    internal static class HotReloadRunDeclaredExtensions
    {
        internal static int Sextuple(this int value, int add)
        {
            return value * 6 + add;
        }

        internal static int Kept(this int value)
        {
            return value;
        }
    }
}

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Host whose lambda a test edits in the same file as <c>HotReloadRunDeclaredExtensions</c>.
    /// </summary>
    public sealed class HotReloadRunDeclaredExtensionHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureValue()
        {
            Func<int> read = () => 30;
            return read();
        }
    }
}
