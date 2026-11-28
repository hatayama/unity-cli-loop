namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Public extension methods that share their names with internal ones of another class and never
    /// apply to the calls the tests make: one takes another receiver type, one takes an <c>int</c>
    /// receiver with another argument type.
    /// </summary>
    public static class HotReloadSameNamePublicExtensionMath
    {
        public static int AddDouble(this string value, object other)
        {
            return value.Length;
        }

        public static int AddTriple(this int value, string other)
        {
            return value + other.Length;
        }
    }
}
