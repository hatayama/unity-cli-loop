using System.Runtime.CompilerServices;

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

    /// <summary>
    /// An internal base an introduced type derives from, so a call to its compiled member shows
    /// the override the artifact declares is dispatched to.
    /// </summary>
    internal abstract class HotReloadInternalAccessBase
    {
        public abstract int Read();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadThroughBase()
        {
            return Read() + 100;
        }
    }

    /// <summary>
    /// A compiled internal type whose body a test edits exactly like the body of an introduced
    /// internal type, so the two outcomes can be compared.
    /// </summary>
    internal sealed class HotReloadInternalBodyEditFixture
    {
        // Why NoInlining: the test reads the edited body back through the patched caller, which
        // an inlined copy at the call site would not observe.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return 1;
        }
    }
}
