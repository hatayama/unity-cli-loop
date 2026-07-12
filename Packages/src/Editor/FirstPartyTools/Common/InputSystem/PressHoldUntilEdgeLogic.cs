#nullable enable

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure wait-loop decisions for Press holds that may extend until wasPressedThisFrame is observed.
    /// Why: releasing as soon as duration/min frames elapse can drop the down state before gameplay
    /// polls it; extending release is safe because down/up counts stay the same (no reinjection).
    /// </summary>
    internal static class PressHoldUntilEdgeLogic
    {
        /// <summary>
        /// Returns whether the duration + minimum observation frame requirements are already satisfied.
        /// </summary>
        public static bool IsBaseWaitSatisfied(
            int observedFrames,
            int minimumObservationFrames,
            float elapsedSeconds,
            float durationSeconds)
        {
            return observedFrames >= minimumObservationFrames && elapsedSeconds >= durationSeconds;
        }

        /// <summary>
        /// Returns whether the hold should continue after the base wait, waiting for a press edge.
        /// </summary>
        public static bool ShouldExtendHoldForEdge(
            bool pressEdgeObserved,
            bool baseWaitSatisfied,
            long elapsedMilliseconds,
            int timeoutMilliseconds)
        {
            if (pressEdgeObserved)
            {
                return false;
            }

            if (!baseWaitSatisfied)
            {
                return false;
            }

            return elapsedMilliseconds < timeoutMilliseconds;
        }

        /// <summary>
        /// Returns how many observation frames were spent only after the base wait completed.
        /// </summary>
        public static int CountExtendedFrames(int observedFrames, int baseSatisfiedFrameCount)
        {
            if (observedFrames <= baseSatisfiedFrameCount)
            {
                return 0;
            }

            return observedFrames - baseSatisfiedFrameCount;
        }
    }
}
