#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Shared helper for applying Input System state changes at the correct update phase.
    // Both keyboard and mouse simulation need frame-precise timing so that
    // wasPressedThisFrame / wasReleasedThisFrame detect the injected state.
    /// <summary>
    /// Provides helper operations for Input System Update behavior.
    /// </summary>
    internal static class InputSystemUpdateHelper
    {
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
                return 1;
            }

            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (targetUpdateType != InputUpdateType.Manual)
            {
                return 2;
            }

            // Manual-mode projects often call InputSystem.Update from their own Update loop,
            // so zero-duration taps need one extra frame to remain visible to gameplay code.
            return 3;
        }

        public static async Task WaitForObservationFrames(CancellationToken ct)
        {
            await WaitForRuntimeFrames(GetMinimumObservationFrameCount(), ct);
        }

        public static async Task WaitForPressLifetime(float duration, CancellationToken ct)
        {
            int minimumObservationFrames = GetMinimumObservationFrameCount();
            int startFrameCount = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            int observedFrames = 0;

            while (observedFrames < minimumObservationFrames || elapsed < duration)
            {
                await EditorDelay.DelayFrame(1, ct);
                observedFrames = Time.frameCount - startFrameCount;
                elapsed = Time.realtimeSinceStartup - startTime;
            }
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

        private static async Task WaitForRuntimeFrames(int frameCount, CancellationToken ct)
        {
            int startFrameCount = Time.frameCount;
            int observedFrames = 0;

            while (observedFrames < frameCount)
            {
                await EditorDelay.DelayFrame(1, ct);
                observedFrames = Time.frameCount - startFrameCount;
            }
        }
    }
}
#endif
