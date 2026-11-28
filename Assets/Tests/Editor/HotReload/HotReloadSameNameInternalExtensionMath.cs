namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension methods whose names a public extension method of another class, or a public
    /// instance method of the receiver's type, shares.
    /// </summary>
    /// <remarks>
    /// Why names of their own: a public extension method named like an existing fixture method would
    /// change the outcome of every other test that calls that method, and one name per public
    /// counterpart keeps each compiler error the tests rely on apart.
    /// </remarks>
    internal static class HotReloadSameNameInternalExtensionMath
    {
        internal static int AddDouble(this int value, int add)
        {
            return value * 2 + add;
        }

        internal static int AddTriple(this int value, int add)
        {
            return value * 3 + add;
        }

        internal static int AddQuadruple(this int value, int add)
        {
            return value * 4 + add;
        }

        internal static int AddQuintuple(this int value, int add)
        {
            return value * 5 + add;
        }

        internal static int Measure(this object value, int add)
        {
            return add + 1000;
        }
    }
}
