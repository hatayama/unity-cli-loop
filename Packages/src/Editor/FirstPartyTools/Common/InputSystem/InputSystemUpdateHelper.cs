#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
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
        private const int DefaultApplyTimeoutMilliseconds = UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS;
        private const int DefaultFrameObservationTimeoutMilliseconds = UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS;

        private static Func<bool> isPausedProvider = () => EditorApplication.isPaused;
        private static int applyTimeoutMilliseconds = DefaultApplyTimeoutMilliseconds;
        private static int frameObservationTimeoutMilliseconds = DefaultFrameObservationTimeoutMilliseconds;

        internal static int PendingConfiguredUpdateCallbackCountValue;

        internal static int ApplyTimeoutMilliseconds => applyTimeoutMilliseconds;

        internal static int FrameObservationTimeoutMilliseconds => frameObservationTimeoutMilliseconds;

        public static async Task<InputSimulationWaitOutcome> ApplyOnNextConfiguredUpdate(Action apply, CancellationToken ct)
        {
            return await InputSystemConfiguredUpdateApplier.ApplyOnNextConfiguredUpdate(apply, ct)
                .ConfigureAwait(false);
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
            PressLifetimeWaitResult result = await WaitForPressLifetime(
                duration,
                isPressEdgeObserved: null,
                ct).ConfigureAwait(false);
            return result.Outcome;
        }

        /// <summary>
        /// Outcome of a Press duration wait, including how many frames release was delayed for edge observation.
        /// </summary>
        internal readonly struct PressLifetimeWaitResult
        {
            public PressLifetimeWaitResult(InputSimulationWaitOutcome outcome, int extendedObservationFrames)
            {
                Outcome = outcome;
                ExtendedObservationFrames = extendedObservationFrames;
            }

            public InputSimulationWaitOutcome Outcome { get; }

            public int ExtendedObservationFrames { get; }
        }

        /// <summary>
        /// Waits for duration + min frames, then optionally holds release until a press edge is observed.
        /// Why: when wasPressedThisFrame never becomes true in the normal window, delaying release
        /// gives gameplay more frames without reinjecting down/up (which would double-fire actions).
        /// </summary>
        public static async Task<PressLifetimeWaitResult> WaitForPressLifetime(
            float duration,
            Func<bool>? isPressEdgeObserved,
            CancellationToken ct)
        {
            return await InputSystemRuntimeFrameWaiter.WaitForPressLifetime(duration, isPressEdgeObserved, ct)
                .ConfigureAwait(false);
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

        public static async Task<InputSimulationWaitOutcome> WaitForRuntimeFrames(int frameCount, CancellationToken ct)
        {
            return await InputSystemRuntimeFrameWaiter.WaitForRuntimeFrames(frameCount, ct).ConfigureAwait(false);
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

        internal static int PendingConfiguredUpdateCallbackCount => Volatile.Read(ref PendingConfiguredUpdateCallbackCountValue);

        internal static bool IsPaused()
        {
            return isPausedProvider();
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
    }
}
#endif
