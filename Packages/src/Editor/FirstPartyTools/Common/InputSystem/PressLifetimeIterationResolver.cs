#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of one WaitForPressLifetime loop iteration, decided after a pause-aware
    /// per-frame wait resolves.
    /// </summary>
    internal enum PressLifetimeIterationDecision
    {
        Continue = 0,
        Completed = 1,
        Paused = 2,
        TimedOut = 3
    }

    /// <summary>
    /// Pure decision logic for one WaitForPressLifetime loop iteration, extracted from
    /// InputSystemRuntimeFrameWaiter so the pause-priority rules can be unit tested without
    /// driving the Editor player loop.
    /// </summary>
    internal static class PressLifetimeIterationResolver
    {
        /// <summary>
        /// Resolves what a WaitForPressLifetime iteration should do once the pause-aware
        /// per-frame wait has returned. A pause takes priority over an already-satisfied
        /// duration, whether observed directly from the frame-wait race
        /// (<paramref name="frameWaitOutcome"/> is Paused) or defensively re-checked after a
        /// timeout that coincided with <paramref name="baseWaitSatisfied"/>
        /// (<paramref name="isPausedFallback"/>) — the second case exists because a timeout and
        /// a pause can both become true in the same real-time window, and this defends against a
        /// completed-duration check absorbing a pause that raced it.
        /// </summary>
        public static PressLifetimeIterationDecision ResolvePostWaitOutcome(
            InputSimulationWaitOutcome frameWaitOutcome,
            bool baseWaitSatisfied,
            bool isPausedFallback)
        {
            if (frameWaitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return PressLifetimeIterationDecision.Paused;
            }

            if (frameWaitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                if (baseWaitSatisfied)
                {
                    return isPausedFallback
                        ? PressLifetimeIterationDecision.Paused
                        : PressLifetimeIterationDecision.Completed;
                }

                return PressLifetimeIterationDecision.TimedOut;
            }

            return PressLifetimeIterationDecision.Continue;
        }
    }
}
#endif
