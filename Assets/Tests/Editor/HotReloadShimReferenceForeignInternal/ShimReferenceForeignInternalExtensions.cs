namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferenceForeignInternal
{
    /// <summary>
    /// An internal type declaring an extension method with the signature of a public one beside it.
    /// This assembly grants no internals, so a referencing assembly never sees this type and binds
    /// the call to the public method.
    /// </summary>
    internal static class ForeignHiddenIntExtensions
    {
        public static int Tripled(this int value)
        {
            return value * 3;
        }
    }

    /// <summary>
    /// The public extension methods a referencing assembly binds its calls to.
    /// </summary>
    public static class ForeignVisibleIntExtensions
    {
        public static int Tripled(this int value)
        {
            return value + value + value;
        }

        public static int Quadrupled(this int value)
        {
            return value * 4;
        }
    }

    /// <summary>
    /// A public type whose internal extension method has the signature of a public one beside it,
    /// so a referencing assembly never sees this member and binds the call to the public method.
    /// </summary>
    public static class ForeignHostWithInternalExtension
    {
        internal static int Quadrupled(this int value)
        {
            return value + value + value + value;
        }
    }
}
