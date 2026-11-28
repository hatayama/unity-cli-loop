namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Public extension methods that share their names with internal ones of another class and never
    /// apply to the calls the tests make: one takes another receiver type, the others take an
    /// <c>int</c> receiver with another argument type, fewer parameters or more parameters.
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

        public static int AddQuadruple(this int value)
        {
            return value;
        }

        public static int AddQuintuple(this int value, int add, int extra)
        {
            return value + add + extra;
        }
    }

    /// <summary>
    /// Public type whose public instance method shares its name with an internal extension method
    /// of another class and does not take an <c>int</c> argument.
    /// </summary>
    public class HotReloadSameNameInstanceTarget
    {
        public int Measure(string text)
        {
            return text.Length;
        }
    }
}
