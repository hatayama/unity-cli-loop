#nullable enable
using System;
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

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Input System recording for the bundled record-input tool.
    /// </summary>
    public class RecordInputUseCase : IUnityCliLoopRecordInputService
    {
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<UnityCliLoopRecordInputResult> RecordInputAsync(
            UnityCliLoopRecordInputRequest request,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new UnityCliLoopRecordInputResult
            {
                Success = false,
                Message = "record-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
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

            UnityCliLoopRecordInputResult response;

            switch (request.Action)
            {
                case RecordInputAction.Start:
                    response = await ExecuteStartAsync(request, ct);
                    break;

                case RecordInputAction.Stop:
                    response = ExecuteStop(request);
                    break;

                default:
                    throw new ArgumentException($"Unknown record-input action: {request.Action}");
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
        private static async Task<UnityCliLoopRecordInputResult> ExecuteStartAsync(UnityCliLoopRecordInputRequest request, CancellationToken ct)
        {
            if (!EditorApplication.isPlaying)
            {
                return new UnityCliLoopRecordInputResult
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new UnityCliLoopRecordInputResult
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before recording input.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (InputRecorder.IsRecording)
            {
                return new UnityCliLoopRecordInputResult
                {
                    Success = false,
                    Message = "Already recording. Stop the current recording first.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (InputReplayer.IsReplaying)
            {
                return new UnityCliLoopRecordInputResult
                {
                    Success = false,
                    Message = "Cannot record while replaying. Stop the replay first.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                return new UnityCliLoopRecordInputResult
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
                RecordInputOverlayState.StartCountdown(delaySeconds);

                try
                {
                    await TimerDelay.WaitThenExecuteOnMainThread(delaySeconds * 1000, () =>
                    {
                        if (!EditorApplication.isPlaying || RecordInputOverlayState.Phase != RecordInputOverlayPhase.Countdown)
                        {
                            RecordInputOverlayState.Clear();
                            return;
                        }

                        RecordInputOverlayState.StartRecording();
                        InputRecorder.StartRecording(keyFilter);
                    }, ct);
                }
                finally
                {
                    // Cancelled mid-countdown: clear stale countdown state so next Start isn't blocked
                    if (!InputRecorder.IsRecording &&
                        RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
                    {
                        RecordInputOverlayState.Clear();
                    }
                }

                if (!EditorApplication.isPlaying || !InputRecorder.IsRecording)
                {
                    return new UnityCliLoopRecordInputResult
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
            return new UnityCliLoopRecordInputResult
            {
                Success = true,
                Message = $"Recording started{filterMessage}{delayMessage}. Use Stop to save.",
                Action = RecordInputAction.Start.ToString()
            };
        }

        private static UnityCliLoopRecordInputResult ExecuteStop(UnityCliLoopRecordInputRequest request)
        {
            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                RecordInputOverlayState.Clear();
                return new UnityCliLoopRecordInputResult
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
                    return new UnityCliLoopRecordInputResult
                    {
                        Success = true,
                        Message = $"Recording was auto-saved at duration limit: {savedPath}",
                        Action = RecordInputAction.Stop.ToString(),
                        OutputPath = savedPath
                    };
                }

                return new UnityCliLoopRecordInputResult
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

            return new UnityCliLoopRecordInputResult
            {
                Success = true,
                Message = $"Recording saved: {eventCount} events across {data.Metadata.TotalFrames} frames ({data.Metadata.DurationSeconds:F1}s)",
                Action = RecordInputAction.Stop.ToString(),
                OutputPath = outputPath,
                TotalFrames = data.Metadata.TotalFrames,
                DurationSeconds = data.Metadata.DurationSeconds
            };
        }
#endif
    }
}
