using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for added-method apply E2E. Kept separate from HotReloadE2EFixture so
    /// twelve-method patch suites cannot interfere with Added registration.
    /// </summary>
    public class HotReloadAddedMethodApplyFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Unrelated(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingCaller(int value)
        {
            return value;
        }
    }
}
