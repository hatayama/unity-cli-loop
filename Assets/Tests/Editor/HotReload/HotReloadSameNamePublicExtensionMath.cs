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

        // Why the parameter is named like the internal one's: a named argument then fails only on
        // its type, as a positional one does.
        public static int AddTriple(this int value, string add)
        {
            return value + add.Length;
        }
    }
}
