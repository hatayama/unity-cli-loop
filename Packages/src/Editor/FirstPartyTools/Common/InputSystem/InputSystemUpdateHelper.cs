#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports whether input observation finished normally, paused, or exceeded the wall-clock guard.
    /// </summary>
    internal enum InputSimulationWaitOutcome
    {
        Completed = 0,
        Paused = 1,
        TimedOut = 2
    }

    // Shared helper for applying Input System state changes at the correct update phase.
    // Both keyboard and mouse simulation need frame-precise timing so that
    // wasPressedThisFrame / wasReleasedThisFrame detect the injected state.
    /// <summary>
    /// Provides helper operations for Input System Update behavior.
    /// </summary>
    internal static class InputSystemUpdateHelper
    {
        private const int StandardPressObservationFrames = 2;
        private const int ManualPressObservationFrames = 3;
        private const int DefaultApplyTimeoutMilliseconds = 5000;
        private const int DefaultFrameObservationTimeoutMilliseconds = 5000;
        private const int PressDurationTimeoutGraceMilliseconds = 5000;
        private static Func<bool> isPausedProvider = () => EditorApplication.isPaused;
        private static int applyTimeoutMilliseconds = DefaultApplyTimeoutMilliseconds;
        private static int frameObservationTimeoutMilliseconds = DefaultFrameObservationTimeoutMilliseconds;

        public static async Task<InputSimulationWaitOutcome> ApplyOnNextConfiguredUpdate(Action apply, CancellationToken ct)
        {
            Debug.Assert(apply != null, "apply must not be null");
            if (apply == null)
            {
                throw new ArgumentNullException(nameof(apply));
            }

            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (InputUpdateTypeResolver.RequiresExplicitUpdate())
            {
                return ApplyOnExplicitUpdate(apply, targetUpdateType, ct);
            }

            TaskCompletionSource<InputSimulationWaitOutcome> tcs = new();
            CancellationTokenRegistration registration = default;
            int isFinished = 0;
            Action? callback = null;

            callback = () =>
            {
                Debug.Assert(callback != null, "callback must be assigned before subscription");
                if (Interlocked.CompareExchange(ref isFinished, 0, 0) != 0)
                {
                    InputSystem.onBeforeUpdate -= callback;
                    registration.Dispose();
                    return;
                }

                InputUpdateType currentUpdateType = InputState.currentUpdateType;
                if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
                {
                    return;
                }

                if (Interlocked.Exchange(ref isFinished, 1) != 0)
                {
                    InputSystem.onBeforeUpdate -= callback;
                    registration.Dispose();
                    return;
                }

                InputSystem.onBeforeUpdate -= callback;
                registration.Dispose();
                apply();
                tcs.TrySetResult(InputSimulationWaitOutcome.Completed);
            };

            InputSystem.onBeforeUpdate += callback;
            if (ct.CanBeCanceled)
            {
                registration = ct.Register(() =>
                {
                    Interlocked.Exchange(ref isFinished, 1);
                    tcs.TrySetCanceled(ct);
                });
            }

            Task timeoutTask = TimerDelay.Wait(applyTimeoutMilliseconds, ct);
            Task completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
            if (completedTask == timeoutTask)
            {
                await timeoutTask.ConfigureAwait(false);
                Interlocked.Exchange(ref isFinished, 1);
                return InputSimulationWaitOutcome.TimedOut;
            }

            InputSimulationWaitOutcome outcome = await tcs.Task.ConfigureAwait(false);
            await SwitchToMainThreadIfNeeded(ct);
            return outcome;
        }

        public static int GetMinimumObservationFrameCount()
        {
            if (!InputUpdateTypeResolver.RequiresExplicitUpdate())
            {
                // Press must survive more than the input update that injected it.
                // CLI follow-up commands can run immediately after completion, so
                // gameplay Update polling needs a second runtime frame before release.
                return StandardPressObservationFrames;
            }

            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (targetUpdateType != InputUpdateType.Manual)
            {
                return StandardPressObservationFrames;
            }

            // Manual-mode projects often call InputSystem.Update from their own Update loop,
            // so zero-duration taps need one extra frame to remain visible to gameplay code.
            return ManualPressObservationFrames;
        }

        public static async Task<InputSimulationWaitOutcome> WaitForObservationFrames(CancellationToken ct)
        {
            return await WaitForRuntimeFrames(GetMinimumObservationFrameCount(), ct).ConfigureAwait(false);
        }

        public static async Task<InputSimulationWaitOutcome> WaitForPressLifetime(float duration, CancellationToken ct)
        {
            await SwitchToMainThreadIfNeeded(ct);

            int minimumObservationFrames = GetMinimumObservationFrameCount();
            int startFrameCount = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            int observedFrames = 0;
            int timeoutMilliseconds = GetPressLifetimeTimeoutMilliseconds(duration);
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (observedFrames < minimumObservationFrames || elapsed < duration)
            {
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                InputSimulationWaitOutcome frameOutcome = await WaitOneRuntimeFrameOrTimeout(
                    timeoutMilliseconds,
                    stopwatch,
                    ct).ConfigureAwait(false);
                if (frameOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    return InputSimulationWaitOutcome.TimedOut;
                }

                await SwitchToMainThreadIfNeeded(ct);
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                observedFrames = Time.frameCount - startFrameCount;
                elapsed = Time.realtimeSinceStartup - startTime;
            }

            return InputSimulationWaitOutcome.Completed;
        }

        public static void RunExplicitUpdate(InputUpdateType targetUpdateType)
        {
            InputSettings? settings = InputSystem.settings;
            if (settings == null)
            {
                InputSystem.Update();
                return;
            }

            InputSettings.UpdateMode originalUpdateMode = settings.updateMode;
            InputSettings.UpdateMode targetUpdateMode = GetExplicitUpdateMode(targetUpdateType, originalUpdateMode);
            if (targetUpdateMode == originalUpdateMode)
            {
                InputSystem.Update();
                return;
            }

            settings.updateMode = targetUpdateMode;
            try
            {
                InputSystem.Update();
            }
            finally
            {
                settings.updateMode = originalUpdateMode;
            }
        }

        private static InputSimulationWaitOutcome ApplyOnExplicitUpdate(
            Action apply,
            InputUpdateType targetUpdateType,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            Action? callback = null;
            bool applied = false;

            callback = () =>
            {
                InputUpdateType currentUpdateType = InputState.currentUpdateType;
                if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
                {
                    return;
                }

                Debug.Assert(callback != null, "callback must be assigned before subscription");
                InputSystem.onBeforeUpdate -= callback;
                apply();
                applied = true;
            };

            InputSystem.onBeforeUpdate += callback;

            RunExplicitUpdate(targetUpdateType);
            if (!applied)
            {
                Debug.Assert(callback != null, "callback must be assigned before explicit update fallback");
                InputSystem.onBeforeUpdate -= callback;
                apply();
                RunExplicitUpdate(targetUpdateType);
            }

            return InputSimulationWaitOutcome.Completed;
        }

        private static InputSettings.UpdateMode GetExplicitUpdateMode(
            InputUpdateType targetUpdateType,
            InputSettings.UpdateMode fallbackUpdateMode)
        {
            if (targetUpdateType == InputUpdateType.Dynamic)
            {
                return InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            }

            if (targetUpdateType == InputUpdateType.Fixed)
            {
                return InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            }

            if (targetUpdateType == InputUpdateType.Manual)
            {
                return InputSettings.UpdateMode.ProcessEventsManually;
            }

            return fallbackUpdateMode;
        }

        public static async Task<InputSimulationWaitOutcome> WaitForRuntimeFrames(int frameCount, CancellationToken ct)
        {
            Debug.Assert(frameCount >= 0, "frameCount must be non-negative");
            await SwitchToMainThreadIfNeeded(ct);

            int startFrameCount = Time.frameCount;
            int observedFrames = 0;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (observedFrames < frameCount)
            {
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                InputSimulationWaitOutcome frameOutcome = await WaitOneRuntimeFrameOrTimeout(
                    frameObservationTimeoutMilliseconds,
                    stopwatch,
                    ct).ConfigureAwait(false);
                if (frameOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    return InputSimulationWaitOutcome.TimedOut;
                }

                await SwitchToMainThreadIfNeeded(ct);
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                observedFrames = Time.frameCount - startFrameCount;
            }

            return InputSimulationWaitOutcome.Completed;
        }

        public static InputSystemMainThreadAwaitable SwitchToMainThreadIfNeeded(CancellationToken ct)
        {
            return new InputSystemMainThreadAwaitable(ct);
        }

        internal static void ConfigurePauseProviderForTests(Func<bool> provider)
        {
            Debug.Assert(provider != null, "provider must not be null");
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            isPausedProvider = provider;
        }

        internal static void ResetPauseProviderForTests()
        {
            isPausedProvider = () => EditorApplication.isPaused;
        }

        internal static void ConfigureTimeoutsForTests(int applyTimeoutMs, int frameObservationTimeoutMs)
        {
            Debug.Assert(applyTimeoutMs > 0, "applyTimeoutMs must be positive");
            Debug.Assert(frameObservationTimeoutMs > 0, "frameObservationTimeoutMs must be positive");
            if (applyTimeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(applyTimeoutMs));
            }

            if (frameObservationTimeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameObservationTimeoutMs));
            }

            applyTimeoutMilliseconds = applyTimeoutMs;
            frameObservationTimeoutMilliseconds = frameObservationTimeoutMs;
        }

        internal static void ResetTimeoutsForTests()
        {
            applyTimeoutMilliseconds = DefaultApplyTimeoutMilliseconds;
            frameObservationTimeoutMilliseconds = DefaultFrameObservationTimeoutMilliseconds;
        }

        private static bool IsPaused()
        {
            return isPausedProvider();
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

            Task frameTask = EditorFrameWaiter.WaitFramesAsync(1, ct);
            Task timeoutTask = TimerDelay.Wait(remainingMilliseconds, ct);
            Task completedTask = await Task.WhenAny(frameTask, timeoutTask).ConfigureAwait(false);
            if (completedTask == timeoutTask)
            {
                await timeoutTask.ConfigureAwait(false);
                return InputSimulationWaitOutcome.TimedOut;
            }

            await frameTask.ConfigureAwait(false);
            return InputSimulationWaitOutcome.Completed;
        }

        private static int GetPressLifetimeTimeoutMilliseconds(float duration)
        {
            double durationMilliseconds = Math.Ceiling(duration * 1000d);
            double timeoutMilliseconds = durationMilliseconds + PressDurationTimeoutGraceMilliseconds;
            if (timeoutMilliseconds > int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(frameObservationTimeoutMilliseconds, (int)timeoutMilliseconds);
        }
    }

    /// <summary>
    /// Switches input simulation continuations back to Unity's main thread without wrapping the switch in a Task.
    /// </summary>
    internal readonly struct InputSystemMainThreadAwaitable
    {
        private readonly CancellationToken _ct;

        public InputSystemMainThreadAwaitable(CancellationToken ct)
        {
            _ct = ct;
        }

        public Awaiter GetAwaiter()
        {
            return new Awaiter(_ct);
        }

        public readonly struct Awaiter : INotifyCompletion
        {
            private readonly CancellationToken _ct;

            public Awaiter(CancellationToken ct)
            {
                _ct = ct;
            }

            public bool IsCompleted
            {
                get
                {
                    _ct.ThrowIfCancellationRequested();
                    return MainThreadSwitcher.IsMainThread;
                }
            }

            public void GetResult()
            {
                _ct.ThrowIfCancellationRequested();
            }

            public void OnCompleted(Action continuation)
            {
                MainThreadSwitcher.SwitchToMainThread(_ct).GetAwaiter().OnCompleted(continuation);
            }
        }
    }
}
#endif
