#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies input mutations on the next Input System update that matches the configured update type.
    /// </summary>
    internal static class InputSystemConfiguredUpdateApplier
    {
        private const int ApplyWaitStateWaiting = 0;
        private const int ApplyWaitStateApplying = 1;
        private const int ApplyWaitStateFinishedWithoutApply = 2;

        public static async Task<InputSimulationWaitOutcome> ApplyOnNextConfiguredUpdate(
            Action apply,
            CancellationToken ct)
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

            TaskCompletionSource<InputSimulationWaitOutcome> tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default;
            int applyWaitState = ApplyWaitStateWaiting;
            Action? callback = null;
            InputSystemUpdateSubscription? subscription = null;

            callback = () =>
            {
                Debug.Assert(callback != null, "callback must be assigned before subscription");
                Debug.Assert(subscription != null, "subscription must be assigned before callback invocation");
                if (Interlocked.CompareExchange(
                        ref applyWaitState,
                        ApplyWaitStateWaiting,
                        ApplyWaitStateWaiting) != ApplyWaitStateWaiting)
                {
                    subscription?.Dispose();
                    return;
                }

                InputUpdateType currentUpdateType = InputState.currentUpdateType;
                if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                        ref applyWaitState,
                        ApplyWaitStateApplying,
                        ApplyWaitStateWaiting) != ApplyWaitStateWaiting)
                {
                    subscription?.Dispose();
                    return;
                }

                subscription?.Dispose();
                try
                {
                    apply();
                    tcs.TrySetResult(InputSimulationWaitOutcome.Completed);
                }
                catch (Exception exception)
                {
                    // Convert apply failures into the awaited task result so timeout paths never wait on an orphaned TCS.
                    tcs.TrySetException(exception);
                }
            };

            subscription = new InputSystemUpdateSubscription(callback);
            try
            {
                if (ct.CanBeCanceled)
                {
                    registration = ct.Register(() =>
                    {
                        if (Interlocked.CompareExchange(
                                ref applyWaitState,
                                ApplyWaitStateFinishedWithoutApply,
                                ApplyWaitStateWaiting) == ApplyWaitStateWaiting)
                        {
                            subscription?.Dispose();
                            tcs.TrySetCanceled(ct);
                        }
                    });
                }

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task timeoutTask = TimerDelay.Wait(
                    InputSystemUpdateHelper.ApplyTimeoutMilliseconds,
                    timeoutCts.Token);
                Task completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
                if (completedTask == timeoutTask)
                {
                    await timeoutTask.ConfigureAwait(false);
                    if (Interlocked.CompareExchange(
                            ref applyWaitState,
                            ApplyWaitStateFinishedWithoutApply,
                            ApplyWaitStateWaiting) == ApplyWaitStateWaiting)
                    {
                        subscription.Dispose();
                        return InputSimulationWaitOutcome.TimedOut;
                    }

                    InputSimulationWaitOutcome appliedOutcome = await tcs.Task.ConfigureAwait(false);
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                    return appliedOutcome;
                }

                timeoutCts.Cancel();
                InputSimulationWaitOutcome outcome = await tcs.Task.ConfigureAwait(false);
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                return outcome;
            }
            finally
            {
                registration.Dispose();
                subscription?.Dispose();
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

            InputSystemUpdateHelper.RunExplicitUpdate(targetUpdateType);
            if (!applied)
            {
                Debug.Assert(callback != null, "callback must be assigned before explicit update fallback");
                InputSystem.onBeforeUpdate -= callback;
                apply();
                InputSystemUpdateHelper.RunExplicitUpdate(targetUpdateType);
            }

            return InputSimulationWaitOutcome.Completed;
        }
    }
}
#endif
