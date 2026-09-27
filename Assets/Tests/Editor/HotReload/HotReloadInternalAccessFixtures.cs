namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Supplies an internal member that generated artifact code cannot normally invoke.
    /// </summary>
    internal static class HotReloadInternalAccessFixture
    {
        // Read by an introduced type in the end-to-end tests: a constant is folded into the
        // caller, so only the compile-time visibility check stands between the two.
        internal const int Constant = 21;

        internal static int Read()
        {
            return 21;
        }

        // Never called: it is what an introduced type still cannot reach, because exposing the
        // internal members of the compiled assembly must leave its private members private.
        private static int Hidden()
        {
            return 5;
        }
    }

    /// <summary>
    /// An internal interface an introduced public type implements, so a call through it shows the
    /// artifact type both loads and dispatches.
    /// </summary>
    internal interface IHotReloadInternalAccessReader
    {
        int Read();
    }
}
