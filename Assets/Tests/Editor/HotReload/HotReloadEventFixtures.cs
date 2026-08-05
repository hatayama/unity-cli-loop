using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled fixture for the field-like event e2e: subscribing must hot-reload while the
    /// raising method is reported as Skipped. The on-disk source path is passed as files[];
    /// edited copies live under Library/UloopHotReload/TestSources/.
    /// </summary>
    public class HotReloadEventFixture
    {
        public event Action ScoreChanged;

        public int HandledCount;

        // Why NoInlining: mirrors HotReloadE2EFixture — assertions must measure the patch
        // detour, not JIT inlining of tiny bodies.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EnableCounting()
        {
            ScoreChanged += HandleScoreChanged;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void HandleScoreChanged()
        {
            HandledCount = HandledCount + 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseScore()
        {
            ScoreChanged?.Invoke();
        }
    }
}
