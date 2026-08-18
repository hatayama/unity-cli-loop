using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled generic method host for skip-reason tests. Identity&lt;T&gt; is a real
    /// generic method so an edit hits EvaluatePatchabilitySkipReason, not the added-generic path.
    /// </summary>
    public sealed class HotReloadGenericMethodSkipFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Identity<T>(int value)
        {
            return value;
        }
    }
}
