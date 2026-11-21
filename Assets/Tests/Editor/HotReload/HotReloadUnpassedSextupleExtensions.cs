namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension class in a file the run-declared extension test does not pass, declaring
    /// the Sextuple that test's edited lambda means once the passed class no longer declares it.
    /// </summary>
    internal static class HotReloadUnpassedSextupleExtensions
    {
        internal static int Sextuple(this int value, int add)
        {
            return value * 6 + add;
        }
    }
}
