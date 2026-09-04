using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled fixture for raising a static field-like event: the shim binds it through
    /// StaticFieldRefAccess, so this exercises that binding at runtime rather than only in the
    /// generated shim text.
    /// </summary>
    public class HotReloadStaticEventFixture
    {
        public static event Action ScoreChanged;

        public static int HandledCount;

        // Why NoInlining: assertions must measure the patch detour, not JIT inlining.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EnableCounting()
        {
            ScoreChanged += HandleScoreChanged;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void HandleScoreChanged()
        {
            HandledCount = HandledCount + 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseScore()
        {
            ScoreChanged?.Invoke();
        }

        // Static event state outlives an instance, so a test must clear it before subscribing.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ResetCounting()
        {
            ScoreChanged = null;
            HandledCount = 0;
        }
    }
}
