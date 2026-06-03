#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports whether input observation finished normally or stopped because Unity paused.
    /// </summary>
    internal enum InputSimulationWaitOutcome
    {
        Completed = 0,
        Paused = 1
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
        private static Func<bool> isPausedProvider = () => EditorApplication.isPaused;

        public static Task ApplyOnNextConfiguredUpdate(Action apply, CancellationToken ct)
        {
            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (InputUpdateTypeResolver.RequiresExplicitUpdate())
            {
                return ApplyOnExplicitUpdate(apply, targetUpdateType, ct);
            }

            TaskCompletionSource<bool> tcs = new();
            CancellationTokenRegistration registration = default;
            Action? callback = null;

            callback = () =>
            {
                InputUpdateType currentUpdateType = InputState.currentUpdateType;
                if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
                {
                    return;
                }

                Debug.Assert(callback != null, "callback must be assigned before subscription");
                InputSystem.onBeforeUpdate -= callback;
                registration.Dispose();
                apply();
                tcs.TrySetResult(true);
            };

            InputSystem.onBeforeUpdate += callback;
            if (ct.CanBeCanceled)
            {
                registration = ct.Register(() =>
                {
                    Debug.Assert(callback != null, "callback must be assigned before cancellation");
                    InputSystem.onBeforeUpdate -= callback;
                    tcs.TrySetCanceled(ct);
                });
            }

            return tcs.Task;
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
            return await WaitForRuntimeFrames(GetMinimumObservationFrameCount(), ct);
        }

        public static async Task<InputSimulationWaitOutcome> WaitForPressLifetime(float duration, CancellationToken ct)
        {
            int minimumObservationFrames = GetMinimumObservationFrameCount();
            int startFrameCount = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            int observedFrames = 0;

            while (observedFrames < minimumObservationFrames || elapsed < duration)
            {
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                await EditorDelay.DelayFrame(1, ct);
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

        private static Task ApplyOnExplicitUpdate(Action apply, InputUpdateType targetUpdateType, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            TaskCompletionSource<bool> tcs = new();
            CancellationTokenRegistration registration = default;
            Action? callback = null;

            callback = () =>
            {
                InputUpdateType currentUpdateType = InputState.currentUpdateType;
                if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
                {
                    return;
                }

                Debug.Assert(callback != null, "callback must be assigned before subscription");
                InputSystem.onBeforeUpdate -= callback;
                registration.Dispose();
                apply();
                tcs.TrySetResult(true);
            };

            InputSystem.onBeforeUpdate += callback;
            if (ct.CanBeCanceled)
            {
                registration = ct.Register(() =>
                {
                    Debug.Assert(callback != null, "callback must be assigned before cancellation");
                    InputSystem.onBeforeUpdate -= callback;
                    tcs.TrySetCanceled(ct);
                });
            }

            RunExplicitUpdate(targetUpdateType);
            if (!tcs.Task.IsCompleted)
            {
                Debug.Assert(callback != null, "callback must be assigned before explicit update fallback");
                InputSystem.onBeforeUpdate -= callback;
                registration.Dispose();
                apply();
                RunExplicitUpdate(targetUpdateType);
                tcs.TrySetResult(true);
            }

            return tcs.Task;
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
            int startFrameCount = Time.frameCount;
            int observedFrames = 0;

            while (observedFrames < frameCount)
            {
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                await EditorDelay.DelayFrame(1, ct);
                if (IsPaused())
                {
                    return InputSimulationWaitOutcome.Paused;
                }

                observedFrames = Time.frameCount - startFrameCount;
            }

            return InputSimulationWaitOutcome.Completed;
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

        private static bool IsPaused()
        {
            return isPausedProvider();
        }
    }
}
#endif
