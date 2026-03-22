#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace io.github.hatayama.uLoopMCP
{
    [McpTool(Description = "Record keyboard and mouse input during PlayMode. Captures key presses, mouse movement, clicks, and scroll events frame-by-frame into a JSON file for later replay.")]
    public class RecordInputTool : AbstractUnityTool<RecordInputSchema, RecordInputResponse>
    {
        public override string ToolName => "record-input";

        protected override async Task<RecordInputResponse> ExecuteAsync(
            RecordInputSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string correlationId = McpConstants.GenerateCorrelationId();

            VibeLogger.LogInfo(
                "record_input_start",
                "Record input started",
                new { Action = parameters.Action.ToString() },
                correlationId: correlationId
            );

            RecordInputResponse response;

            switch (parameters.Action)
            {
                case RecordInputAction.Start:
                    response = await ExecuteStartAsync(parameters, ct);
                    break;

                case RecordInputAction.Stop:
                    response = ExecuteStop(parameters);
                    break;

                default:
                    throw new ArgumentException($"Unknown record-input action: {parameters.Action}");
            }

            VibeLogger.LogInfo(
                "record_input_complete",
                $"Record input completed: {response.Message}",
                new { Action = parameters.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            return response;
        }

        private static async Task<RecordInputResponse> ExecuteStartAsync(RecordInputSchema parameters, CancellationToken ct)
        {
            if (!EditorApplication.isPlaying)
            {
                return new RecordInputResponse
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = RecordInputAction.Start.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new RecordInputResponse
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before recording input.",
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

            int delaySeconds = Mathf.Clamp(parameters.DelaySeconds, RecordInputConstants.MIN_DELAY_SECONDS, RecordInputConstants.MAX_DELAY_SECONDS);
            HashSet<Key>? keyFilter = InputRecordingFileHelper.ParseKeyFilter(parameters.Keys);

            if (parameters.ShowOverlay)
            {
                OverlayCanvasFactory.EnsureExists();
                RecordReplayOverlayFactory.EnsureRecordOverlay();
            }

            if (delaySeconds > 0)
            {
                RecordInputOverlayState.StartCountdown(delaySeconds);

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

                if (!EditorApplication.isPlaying || !InputRecorder.IsRecording)
                {
                    RecordInputOverlayState.Clear();
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

            string filterMessage = keyFilter != null ? $" (filtering: {parameters.Keys})" : "";
            string delayMessage = delaySeconds > 0 ? $" (after {delaySeconds}s countdown)" : "";
            return new RecordInputResponse
            {
                Success = true,
                Message = $"Recording started{filterMessage}{delayMessage}. Use Stop to save.",
                Action = RecordInputAction.Start.ToString()
            };
        }

        private static RecordInputResponse ExecuteStop(RecordInputSchema parameters)
        {
            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
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

            string outputPath = InputRecordingFileHelper.ResolveOutputPath(parameters.OutputPath);
            InputRecordingFileHelper.Save(data, outputPath);

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

    }
}
