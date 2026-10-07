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
    /// A public type whose private and internal members a shim reference copy keeps as they are for
    /// a target the assembly grants no internals to, while its nested type is publicized as before.
    /// </summary>
    public static class ShimReferencePublicHost
    {
        internal static int HiddenField = 1;

        private static int SecretField = 1;

        private static int Secret()
        {
            return SecretField;
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
    /// A public base type for a target the assembly grants no internals to: a shim reference copy
    /// keeps its private protected member as it is, because a derived type in another assembly
    /// reaches that member only through such a grant, and still publicizes its protected member,
    /// because a shim calls it from outside the type hierarchy.
    /// </summary>
    public class ShimReferencePublicBase
    {
        private protected int Guarded()
        {
            return 2;
        }

        protected int Shielded()
        {
            return 3;
        }
    }
}
