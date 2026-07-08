#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Input System recording for the bundled record-input tool.
    /// </summary>
    public class RecordInputUseCase
    {
        // Wire-visible fragment of the paused preflight message; tests pin the composed string.
        public const string PausedActionDescription = "recording input";

#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<RecordInputResponse> RecordInputAsync(
            RecordInputSchema request,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new RecordInputResponse
            {
                Success = false,
                Message = InputSystemPackageRequirementMessage.Format("record-input"),
                Action = request.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            VibeLogger.LogInfo(
                "record_input_start",
                "Record input started",
                new { Action = request.Action.ToString() },
                correlationId: correlationId
            );

            RecordInputResponse response;

            switch (request.Action)
            {
                case RecordInputAction.Start:
                    // Why: delayed-start timeouts must propagate without recapturing Unity's stalled synchronization context.
                    response = await ExecuteStartAsync(request, ct).ConfigureAwait(false);
                    await MainThreadSwitcher.SwitchToMainThread(ct);
                    break;

                case RecordInputAction.Stop:
                    response = ExecuteStop(request);
                    break;

                default:
                    // Only reachable when an out-of-range enum value is cast from an integer;
                    // surface as a Success=false response so the CLI treats it as a validation failure.
                    response = new RecordInputResponse
                    {
                        Success = false,
                        Message = $"Unknown record-input action: {request.Action}",
                        Action = request.Action.ToString()
                    };
                    break;
            }

            VibeLogger.LogInfo(
                "record_input_complete",
                $"Record input completed: {response.Message}",
                new { Action = request.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            return response;
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static int _delayedStartGeneration;

        private static async Task<RecordInputResponse> ExecuteStartAsync(
            RecordInputSchema request,
            CancellationToken ct)
        {
            ValidationResult preflight = PlayModeToolPreflightService.RequireActiveAndNotPaused(PausedActionDescription);
            if (!preflight.IsValid)
            {
                return new RecordInputResponse
                {
                    Success = false,
                    Message = preflight.ErrorMessage,
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (InputRecorder.IsRecording)
            {
                return new RecordInputResponse
                {
                    Success = false,
                    Message = "Already recording. Stop the current recording first.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (InputReplayer.IsReplaying)
            {
                return new RecordInputResponse
                {
                    Success = false,
                    Message = "Cannot record while replaying. Stop the replay first.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                return new RecordInputResponse
                {
                    Success = false,
                    Message = "Recording countdown already in progress.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            int delaySeconds = Mathf.Clamp(request.DelaySeconds, RecordInputConstants.MIN_DELAY_SECONDS, RecordInputConstants.MAX_DELAY_SECONDS);
            HashSet<Key>? keyFilter = InputRecordingFileHelper.ParseKeyFilter(request.Keys);

            if (request.ShowOverlay)
            {
                OverlayCanvasFactory.EnsureExists();
                RecordReplayOverlayFactory.EnsureRecordOverlay();
            }

            if (delaySeconds > 0)
            {
                RecordInputDelayedStartOutcome delayedStartOutcome =
                    await ExecuteDelayedStartAsync(delaySeconds, keyFilter, ct)
                        .ConfigureAwait(false);
                if (delayedStartOutcome != RecordInputDelayedStartOutcome.Started)
                {
                    return new RecordInputResponse
                    {
                        Success = false,
                        Message = "Recording cancelled (PlayMode ended during countdown).",
                        Action = RecordInputAction.Start.ToString()
                    };
                }
            }
            else
            {
                RecordInputOverlayState.StartRecording();
                InputRecorder.StartRecording(keyFilter);
            }

            string filterMessage = keyFilter != null ? $" (filtering: {request.Keys})" : "";
            string delayMessage = delaySeconds > 0 ? $" (after {delaySeconds}s countdown)" : "";
            return new RecordInputResponse
            {
                Success = true,
                Message = $"Recording started{filterMessage}{delayMessage}. Use Stop to save.",
                Action = RecordInputAction.Start.ToString()
            };
        }

        private static RecordInputResponse ExecuteStop(RecordInputSchema request)
        {
            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                Interlocked.Increment(ref _delayedStartGeneration);
                RecordInputOverlayState.Clear();
                return new RecordInputResponse
                {
                    Success = true,
                    Message = "Recording countdown cancelled.",
                    Action = RecordInputAction.Stop.ToString()
                };
            }

            if (!InputRecorder.IsRecording)
            {
                // Recording may have been auto-stopped at the duration limit
                if (InputRecorder.LastAutoSavePath != null)
                {
                    string savedPath = InputRecorder.LastAutoSavePath;
                    InputRecorder.LastAutoSavePath = null;
                    return new RecordInputResponse
                    {
                        Success = true,
                        Message = $"Recording was auto-saved at duration limit: {savedPath}",
                        Action = RecordInputAction.Stop.ToString(),
                        OutputPath = savedPath
                    };
                }

                return new RecordInputResponse
                {
                    Success = false,
                    Message = "Not currently recording. Use Start first.",
                    Action = RecordInputAction.Stop.ToString()
                };
            }

            InputRecordingData data = InputRecorder.StopRecording();

            string outputPath = InputRecordingFileHelper.ResolveOutputPath(request.OutputPath);
            InputRecordingFileHelper.Save(data, outputPath);
            InputRecorder.NotifyRecordingStopped();

            int eventCount = data.GetTotalEventCount();

            return new RecordInputResponse
            {
                Success = true,
                Message = $"Recording saved: {eventCount} events across {data.Metadata.TotalFrames} frames ({data.Metadata.DurationSeconds:F1}s)",
                Action = RecordInputAction.Stop.ToString(),
                OutputPath = outputPath,
                TotalFrames = data.Metadata.TotalFrames,
                DurationSeconds = data.Metadata.DurationSeconds
            };
        }

        private static async Task<RecordInputDelayedStartOutcome> ExecuteDelayedStartAsync(
            int delaySeconds,
            HashSet<Key>? keyFilter,
            CancellationToken ct)
        {
            RecordInputDelayedStartOutcome outcome = RecordInputDelayedStartOutcome.Cancelled;
            bool waitCompleted = false;
            int generation = Interlocked.Increment(ref _delayedStartGeneration);
            RecordInputOverlayState.StartCountdown(delaySeconds);

            try
            {
                await TimerDelay.WaitThenExecuteOnMainThread(delaySeconds * 1000, () =>
                {
                    if (!IsCurrentDelayedStartGeneration(generation))
                    {
                        return;
                    }

                    if (!EditorApplication.isPlaying || RecordInputOverlayState.Phase != RecordInputOverlayPhase.Countdown)
                    {
                        RecordInputOverlayState.Clear();
                        outcome = RecordInputDelayedStartOutcome.Cancelled;
                        return;
                    }

                    RecordInputOverlayState.StartRecording();
                    InputRecorder.StartRecording(keyFilter);
                    outcome = RecordInputDelayedStartOutcome.Started;
                }, ct).ConfigureAwait(false);
                waitCompleted = true;
                return outcome;
            }
            finally
            {
                if (!waitCompleted)
                {
                    QueueCountdownCleanup(generation);
                }
            }
        }

        private enum RecordInputDelayedStartOutcome
        {
            Cancelled = 0,
            Started = 1
        }

        private static void QueueCountdownCleanup(int generation)
        {
            CleanupCountdownOnMainThreadAsync(generation, CancellationToken.None).Forget();
        }

        private static async Task CleanupCountdownOnMainThreadAsync(
            int generation,
            CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!IsCurrentDelayedStartGeneration(generation))
            {
                return;
            }

            // Why: timeout/cancellation can resume off-thread, so stale countdown state is cleared only on Unity's context.
            if (!InputRecorder.IsRecording &&
                RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                RecordInputOverlayState.Clear();
            }
        }

        private static bool IsCurrentDelayedStartGeneration(int generation)
        {
            return Volatile.Read(ref _delayedStartGeneration) == generation;
        }
#endif
    }
}
