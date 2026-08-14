using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for added-field apply E2E. Kept separate from HotReloadE2EFixture and
    /// the added-method fixture so twelve-method patch suites cannot interfere with store state.
    /// Static retainers keep instance identity across CLI execute-dynamic-code snippets so
    /// re-apply can prove stored values survive.
    /// </summary>
    public class HotReloadAddedFieldApplyFixture
    {
        public static HotReloadAddedFieldApplyFixture RetainedA;

        public static HotReloadAddedFieldApplyFixture RetainedB;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadAdded()
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void WriteAdded(int value)
        {
        }
    }
}
