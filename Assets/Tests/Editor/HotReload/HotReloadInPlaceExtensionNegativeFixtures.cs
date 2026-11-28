namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.UnimportedExtensions
{
    /// <summary>
    /// Internal extension class in a namespace the partial fixture does not import, so a call to
    /// its method from that fixture does not compile and must not be patched in place.
    /// </summary>
    internal static class HotReloadUnimportedExtensionMath
    {
        internal static int UnimportedTwice(this int value, int add)
        {
            return value * 2 + add;
        }
    }
}

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal static class with a private extension method, which another type cannot call, so a
    /// call to it from the partial fixture must not be patched in place.
    /// </summary>
    internal static class HotReloadPrivateExtensionMath
    {
        // Keeps the private extension used, so the compiler does not warn about it.
        internal static int UsePrivateTwice()
        {
            return 1.PrivateTwice(1);
        }

        private static int PrivateTwice(this int value, int add)
        {
            return value * 2 + add;
        }
    }
}
