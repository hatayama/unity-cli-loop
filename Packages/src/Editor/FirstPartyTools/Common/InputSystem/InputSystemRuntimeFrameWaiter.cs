#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Waits for runtime frames and press lifetimes while honoring pause and timeout guards.
    /// </summary>
    internal static class InputSystemRuntimeFrameWaiter
    {
        private const int PressDurationTimeoutGraceMilliseconds = 5000;

        public static async Task<InputSimulationWaitOutcome> WaitForRuntimeFrames(int frameCount, CancellationToken ct)
        {
            Debug.Assert(frameCount >= 0, "frameCount must be non-negative");
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);

            int startFrameCount = Time.frameCount;
            int observedFrames = 0;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (observedFrames < frameCount)
            {
                if (InputSystemUpdateHelper.IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                InputSimulationWaitOutcome frameOutcome = await WaitOneRuntimeFrameOrTimeout(
                    InputSystemUpdateHelper.FrameObservationTimeoutMilliseconds,
                    stopwatch,
                    ct).ConfigureAwait(false);
                if (frameOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    return InputSimulationWaitOutcome.TimedOut;
                }

                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                if (InputSystemUpdateHelper.IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                observedFrames = Time.frameCount - startFrameCount;
            }

            return InputSimulationWaitOutcome.Completed;
        }

        public static async Task<InputSystemUpdateHelper.PressLifetimeWaitResult> WaitForPressLifetime(
            float duration,
            Func<bool>? isPressEdgeObserved,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);

            int minimumObservationFrames = InputSystemUpdateHelper.GetMinimumObservationFrameCount();
            int startFrameCount = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            int observedFrames = 0;
            int baseSatisfiedFrameCount = -1;
            int timeoutMilliseconds = GetPressLifetimeTimeoutMilliseconds(duration);
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                bool baseWaitSatisfied = PressHoldUntilEdgeLogic.IsBaseWaitSatisfied(
                    observedFrames,
                    minimumObservationFrames,
                    elapsed,
                    duration);
                if (baseWaitSatisfied && baseSatisfiedFrameCount < 0)
                {
                    baseSatisfiedFrameCount = observedFrames;
                }

                bool pressEdgeObserved = isPressEdgeObserved != null && isPressEdgeObserved();
                bool shouldExtendForEdge = isPressEdgeObserved != null &&
                    PressHoldUntilEdgeLogic.ShouldExtendHoldForEdge(
                        pressEdgeObserved,
                        baseWaitSatisfied,
                        stopwatch.ElapsedMilliseconds,
                        timeoutMilliseconds);

                if (baseWaitSatisfied && !shouldExtendForEdge)
                {
                    int extendedFrames = baseSatisfiedFrameCount < 0
                        ? 0
                        : PressHoldUntilEdgeLogic.CountExtendedFrames(observedFrames, baseSatisfiedFrameCount);
                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(
                        InputSimulationWaitOutcome.Completed,
                        extendedFrames);
                }

                if (InputSystemUpdateHelper.IsPaused())
                {
                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(InputSimulationWaitOutcome.Paused, 0);
                }

                InputSimulationWaitOutcome frameOutcome = await WaitOneRuntimeFrameOrTimeout(
                    timeoutMilliseconds,
                    stopwatch,
                    ct).ConfigureAwait(false);
                if (frameOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    // Why: timeout during edge-extension still completes Press successfully with
                    // PressEdgeObserved=false — same contract as before, not a hard failure.
                    if (baseWaitSatisfied)
                    {
                        int extendedFrames = baseSatisfiedFrameCount < 0
                            ? 0
                            : PressHoldUntilEdgeLogic.CountExtendedFrames(observedFrames, baseSatisfiedFrameCount);
                        return new InputSystemUpdateHelper.PressLifetimeWaitResult(
                            InputSimulationWaitOutcome.Completed,
                            extendedFrames);
                    }

                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(InputSimulationWaitOutcome.TimedOut, 0);
                }

                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                if (InputSystemUpdateHelper.IsPaused())
                {
                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(InputSimulationWaitOutcome.Paused, 0);
                }

                observedFrames = Time.frameCount - startFrameCount;
                elapsed = Time.realtimeSinceStartup - startTime;
            }
        }

        private static async Task<InputSimulationWaitOutcome> WaitOneRuntimeFrameOrTimeout(
            int timeoutMilliseconds,
            System.Diagnostics.Stopwatch stopwatch,
            CancellationToken ct)
        {
            int remainingMilliseconds = timeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0)
            {
                return InputSimulationWaitOutcome.TimedOut;
            }

            bool frameObserved = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                1,
                remainingMilliseconds,
                ct).ConfigureAwait(false);
            return frameObserved
                ? InputSimulationWaitOutcome.Completed
                : InputSimulationWaitOutcome.TimedOut;
        }

        private static int GetPressLifetimeTimeoutMilliseconds(float duration)
        {
            double durationMilliseconds = Math.Ceiling(duration * 1000d);
            double timeoutMilliseconds = durationMilliseconds + PressDurationTimeoutGraceMilliseconds;
            if (timeoutMilliseconds > int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(
                InputSystemUpdateHelper.FrameObservationTimeoutMilliseconds,
                (int)timeoutMilliseconds);
        }
    }
}
#endif
