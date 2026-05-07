#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Input System playback for the bundled replay-input tool.
    /// </summary>
    public class ReplayInputUseCase : IUnityCliLoopReplayInputService
    {
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<UnityCliLoopReplayInputResult> ReplayInputAsync(
            UnityCliLoopReplayInputRequest request,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new UnityCliLoopReplayInputResult
            {
                Success = false,
                Message = "replay-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
                Action = request.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            VibeLogger.LogInfo(
                "replay_input_start",
                "Replay input started",
                new { Action = request.Action.ToString() },
                correlationId: correlationId
            );

            UnityCliLoopReplayInputResult response;

            switch (request.Action)
            {
                case ReplayInputAction.Start:
                    response = ExecuteStart(request);
                    break;

                case ReplayInputAction.Stop:
                    response = ExecuteStop();
                    break;

                case ReplayInputAction.Status:
                    response = ExecuteStatus();
                    break;

                default:
                    throw new ArgumentException($"Unknown replay-input action: {request.Action}");
            }

            VibeLogger.LogInfo(
                "replay_input_complete",
                $"Replay input completed: {response.Message}",
                new { Action = request.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            await Task.CompletedTask;
            return response;
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static UnityCliLoopReplayInputResult ExecuteStart(UnityCliLoopReplayInputRequest request)
        {
            if (!EditorApplication.isPlaying)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before replaying input.",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            if (InputReplayer.IsReplaying)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = "Already replaying. Stop the current replay first.",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            if (InputRecorder.IsRecording)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = "Cannot replay while recording. Stop the recording first.",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            string inputPath = InputRecordingFileHelper.ResolveLatestRecording(request.InputPath);
            if (string.IsNullOrEmpty(inputPath))
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = $"No recording files found in {RecordInputConstants.DEFAULT_OUTPUT_DIR}/",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            if (!File.Exists(inputPath))
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = $"Recording file not found: {inputPath}",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            InputRecordingData? data = InputRecordingFileHelper.Load(inputPath);

            if (data == null || data.Metadata == null)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = $"Failed to parse recording file: {inputPath}",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            OverlayCanvasFactory.EnsureExists();
            RecordReplayOverlayFactory.EnsureReplayOverlay();
            InputReplayer.StartReplay(data, request.Loop, request.ShowOverlay);

            int eventCount = data.GetTotalEventCount();

            return new UnityCliLoopReplayInputResult
            {
                Success = true,
                Message = $"Replay started: {eventCount} events across {data.Metadata.TotalFrames} frames" +
                          (request.Loop ? " (looping)" : ""),
                Action = ReplayInputAction.Start.ToString(),
                InputPath = inputPath,
                TotalFrames = data.Metadata.TotalFrames,
                IsReplaying = true
            };
        }

        private static UnityCliLoopReplayInputResult ExecuteStop()
        {
            if (!InputReplayer.IsReplaying)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = false,
                    Message = "Not currently replaying.",
                    Action = ReplayInputAction.Stop.ToString()
                };
            }

            int stoppedFrame = InputReplayer.CurrentFrame;
            int totalFrames = InputReplayer.TotalFrames;
            InputReplayer.StopReplay();

            return new UnityCliLoopReplayInputResult
            {
                Success = true,
                Message = $"Replay stopped at frame {stoppedFrame}/{totalFrames}",
                Action = ReplayInputAction.Stop.ToString(),
                CurrentFrame = stoppedFrame,
                TotalFrames = totalFrames,
                IsReplaying = false
            };
        }

        private static UnityCliLoopReplayInputResult ExecuteStatus()
        {
            if (!InputReplayer.IsReplaying)
            {
                return new UnityCliLoopReplayInputResult
                {
                    Success = true,
                    Message = "Not replaying.",
                    Action = ReplayInputAction.Status.ToString(),
                    IsReplaying = false
                };
            }

            return new UnityCliLoopReplayInputResult
            {
                Success = true,
                Message = $"Replaying: frame {InputReplayer.CurrentFrame}/{InputReplayer.TotalFrames} ({InputReplayer.Progress:P0})",
                Action = ReplayInputAction.Status.ToString(),
                CurrentFrame = InputReplayer.CurrentFrame,
                TotalFrames = InputReplayer.TotalFrames,
                Progress = InputReplayer.Progress,
                IsReplaying = true
            };
        }
#endif
    }
}
