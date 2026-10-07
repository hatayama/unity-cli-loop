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
    /// A public type whose internal members a shim reference copy keeps internal for a target the
    /// assembly grants no internals to, while its private member and nested type are publicized as
    /// before.
    /// </summary>
    public static class ShimReferencePublicHost
    {
        internal static int HiddenField = 1;

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

    /// <summary>
    /// A public type whose private protected member a shim reference copy keeps as it is for a
    /// target the assembly grants no internals to: a derived type in another assembly reaches that
    /// member only through such a grant.
    /// </summary>
    public class ShimReferencePublicBase
    {
        private protected int Guarded()
        {
            return 2;
        }
    }
}
