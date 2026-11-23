using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferenceForeignInternal
{
    /// <summary>
    /// Public class of an assembly other than the one hot reload patches, whose public overload takes
    /// any value while a private one takes an ArraySegment. The patch's compile keeps this
    /// assembly's private members hidden, because it grants no internals, so a referencing
    /// assembly's call binds to the public overload in the patch as in Unity's compile.
    /// </summary>
    public static class ForeignHiddenPickOverloads
    {
        public static int Pick(object value)
        {
            return 1;
        }

        private static int Pick(ArraySegment<int> segment)
        {
            return segment.Count;
        }
    }
}
