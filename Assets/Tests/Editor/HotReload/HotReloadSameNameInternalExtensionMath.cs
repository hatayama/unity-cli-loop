namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension method whose name a public extension method of another class shares.
    /// </summary>
    /// <remarks>
    /// Why a name of its own: a public extension method named like an existing fixture method would
    /// change the outcome of every other test that calls that method.
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
    }
}
