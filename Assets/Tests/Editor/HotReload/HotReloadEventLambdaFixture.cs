using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled fixture for raising a field-like event from inside a lambda: the closure body
    /// already forces the accessor-rewrite path, so this pins that the event rewrite composes
    /// with it instead of skipping the method.
    /// </summary>
    public class HotReloadEventLambdaFixture
    {
        public event Action ScoreChanged;

        public int HandledCount;

        // Why NoInlining: assertions must measure the patch detour, not JIT inlining.
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
        public void RaiseScoreFromLambda()
        {
            Action raise = () => ScoreChanged?.Invoke();
            raise();
        }
    }
}
