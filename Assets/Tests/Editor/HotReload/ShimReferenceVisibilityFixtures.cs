namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A top-level internal type with an extension method. A shim reference copy of this assembly
    /// built for a target it grants no internals to must keep the type internal: the target's own
    /// compile never saw it, so neither may the shim compile.
    /// </summary>
    internal static class ShimReferenceHiddenIntExtensions
    {
        public static int Doubled(this int value)
        {
            return value * 2;
        }
    }

    /// <summary>
    /// A public type whose internal member a shim reference copy keeps internal for a target the
    /// assembly grants no internals to, while its private member and nested type are publicized as
    /// before.
    /// </summary>
    public static class ShimReferencePublicHost
    {
        private static int Secret()
        {
            return 1;
        }

        internal static int Hidden(this int value)
        {
            return value + 1;
        }

        internal class NestedInternal
        {
        }
    }
}
