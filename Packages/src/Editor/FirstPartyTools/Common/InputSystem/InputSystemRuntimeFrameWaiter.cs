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
                // Polled in addition to the mid-wait race below: a test-only fake pause provider
                // (see InputSystemUpdateHelper.ConfigurePauseProviderForTests) never fires the
                // real EditorApplication.pauseStateChanged event that race relies on, so this
                // poll is what a fake-paused test actually gets caught by.
                if (InputSystemUpdateHelper.IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                // WaitOneRuntimeFrameOrTimeout races a pause signal alongside the frame/timeout
                // wait, so a genuine (non-faked) pause is caught the instant it happens instead
                // of only between completed waits (see EditorPauseAwaiter).
                InputSimulationWaitOutcome frameOutcome = await WaitOneRuntimeFrameOrTimeout(
                    InputSystemUpdateHelper.FrameObservationTimeoutMilliseconds,
                    stopwatch,
                    ct).ConfigureAwait(false);
                if (frameOutcome == InputSimulationWaitOutcome.Paused)
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                if (frameOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    // Why: the wall-clock timeout keeps advancing during a real pause, so a
                    // timeout that coincided with one must still report Paused — otherwise a
                    // pause could be silently absorbed into a TimedOut result (same class of bug
                    // as the Completed-absorption case WaitForPressLifetime guards against).
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                    if (InputSystemUpdateHelper.IsPaused())
                    {
                        return InputSimulationWaitOutcome.Paused;
                    }

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

                // Polled in addition to the mid-wait race below: a test-only fake pause provider
                // (see InputSystemUpdateHelper.ConfigurePauseProviderForTests) never fires the
                // real EditorApplication.pauseStateChanged event that race relies on, so this
                // poll is what a fake-paused test actually gets caught by.
                if (InputSystemUpdateHelper.IsPaused())
                {
                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(InputSimulationWaitOutcome.Paused, 0);
                }

                // WaitOneRuntimeFrameOrTimeout races a pause signal alongside the frame/timeout
                // wait, so a genuine (non-faked) pause is caught the instant it happens instead
                // of only between completed waits (see EditorPauseAwaiter).
                InputSimulationWaitOutcome frameOutcome = await WaitOneRuntimeFrameOrTimeout(
                    timeoutMilliseconds,
                    stopwatch,
                    ct).ConfigureAwait(false);

                // Must switch back to the main thread before reading IsPaused() below: the
                // ConfigureAwait(false) above can resume this continuation on a thread-pool
                // thread even though WaitOneRuntimeFrameOrTimeout ran on the main thread
                // internally.
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);

                // Why: a pause and an already-satisfied duration can both become true within the
                // same real-time window (Time.realtimeSinceStartup keeps advancing while paused),
                // so IsPaused() is re-checked here even when frameOutcome says TimedOut — without
                // it, a pause could be silently absorbed into a Completed result.
                PressLifetimeIterationDecision decision = PressLifetimeIterationResolver.ResolvePostWaitOutcome(
                    frameOutcome,
                    baseWaitSatisfied,
                    InputSystemUpdateHelper.IsPaused());
                if (decision == PressLifetimeIterationDecision.Paused)
                {
                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(InputSimulationWaitOutcome.Paused, 0);
                }

                if (decision == PressLifetimeIterationDecision.Completed)
                {
                    // Why: timeout during edge-extension still completes Press successfully with
                    // PressEdgeObserved=false — same contract as before, not a hard failure.
                    int extendedFrames = baseSatisfiedFrameCount < 0
                        ? 0
                        : PressHoldUntilEdgeLogic.CountExtendedFrames(observedFrames, baseSatisfiedFrameCount);
                    return new InputSystemUpdateHelper.PressLifetimeWaitResult(
                        InputSimulationWaitOutcome.Completed,
                        extendedFrames);
                }

                if (decision == PressLifetimeIterationDecision.TimedOut)
                {
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

        // Races a pause signal alongside the frame/timeout wait so a pause is observed the
        // instant it happens. Without this, a pause that starts and ends entirely inside the
        // frame wait below is invisible to callers until that wait's own timeout expires — and
        // by then real time (which keeps advancing while paused) can have already satisfied a
        // duration/frame-count condition, silently absorbing the pause into a false "completed"
        // result (see PressLifetimeIterationResolver for the caller-side defense in depth).
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

            // Must subscribe on the main thread: EditorPauseAwaiter subscribes to
            // EditorApplication.pauseStateChanged, which is main-thread-only, and a caller's
            // prior ConfigureAwait(false) may have left us on a thread-pool thread.
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);

            using CancellationTokenSource raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<bool> frameTask = EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                1,
                remainingMilliseconds,
                raceCancellation.Token);
            Task pauseTask = EditorPauseAwaiter.WaitForPauseAsync(raceCancellation.Token);

            Task winner = await Task.WhenAny(frameTask, pauseTask).ConfigureAwait(false);

            // Must switch back to the main thread before canceling: EditorPauseAwaiter's
            // cancellation callback unsubscribes EditorApplication.pauseStateChanged, which is
            // main-thread-only, and the ConfigureAwait(false) above may have left us off-thread.
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            raceCancellation.Cancel();

            if (winner == pauseTask)
            {
                return InputSimulationWaitOutcome.Paused;
            }

            bool frameObserved = await frameTask.ConfigureAwait(false);
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
