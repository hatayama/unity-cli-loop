using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal member with a BCL twin type parameter, declared in a file the BCL twin tests never
    /// pass to the worker, so a call to it from an edited body is a use of an unpassed internal member.
    /// </summary>
    public static class HotReloadBclTwinOtherFileTargets
    {
        internal static int SpanLength(ReadOnlySpan<int> span)
        {
            return span.Length;
        }
    }
}
