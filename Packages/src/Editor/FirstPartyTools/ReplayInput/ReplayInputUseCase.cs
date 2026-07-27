#nullable enable
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public class ReplayInputUseCase
    {
        // Wire-visible fragment of the paused preflight message; tests pin the composed string.
        public const string PausedActionDescription = "replaying input";

#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<ReplayInputResponse> ReplayInputAsync(
            ReplayInputSchema request,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new ReplayInputResponse
            {
                Success = false,
                Message = InputSystemPackageRequirementMessage.Format("replay-input"),
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

            ReplayInputResponse response;

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
                    // Only reachable when an out-of-range enum value is cast from an integer;
                    // surface as a Success=false response so the CLI treats it as a validation failure.
                    response = new ReplayInputResponse
                    {
                        Success = false,
                        Message = $"Unknown replay-input action: {request.Action}",
                        Action = request.Action.ToString()
                    };
                    break;
            }

            VibeLogger.LogInfo(
                "replay_input_complete",
                $"Replay input completed: {response.Message}",
                new { Action = request.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            await Task.CompletedTask.ConfigureAwait(false);
            return response;
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static ReplayInputResponse ExecuteStart(ReplayInputSchema request)
        {
            PlayModeToolPreflightResult preflight = PlayModeToolPreflightService.RequireActiveAndNotPaused(PausedActionDescription);
            if (!preflight.IsValid)
            {
                return new ReplayInputResponse
                {
                    Success = false,
                    Message = preflight.ErrorMessage,
                    Action = ReplayInputAction.Start.ToString(),
                    RejectedByActivePausePointId = preflight.RejectedByActivePausePointId
                };
            }

            if (InputReplayer.IsReplaying)
            {
                return new ReplayInputResponse
                {
                    Success = false,
                    Message = "Already replaying. Stop the current replay first.",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            if (InputRecorder.IsRecording)
            {
                return new ReplayInputResponse
                {
                    Success = false,
                    Message = "Cannot replay while recording. Stop the recording first.",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            string inputPath = InputRecordingFileHelper.ResolveLatestRecording(request.InputPath);
            if (string.IsNullOrEmpty(inputPath))
            {
                return new ReplayInputResponse
                {
                    Success = false,
                    Message = $"No recording files found in {RecordInputConstants.DEFAULT_OUTPUT_DIR}/",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            if (!File.Exists(inputPath))
            {
                return new ReplayInputResponse
                {
                    Success = false,
                    Message = $"Recording file not found: {inputPath}",
                    Action = ReplayInputAction.Start.ToString()
                };
            }

            InputRecordingData? data = InputRecordingFileHelper.Load(inputPath);

            if (data == null || data.Metadata == null)
            {
                return new ReplayInputResponse
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

            return new ReplayInputResponse
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

        private static ReplayInputResponse ExecuteStop()
        {
            if (!InputReplayer.IsReplaying)
            {
                return new ReplayInputResponse
                {
                    Success = false,
                    Message = "Not currently replaying.",
                    Action = ReplayInputAction.Stop.ToString()
                };
            }

            int stoppedFrame = InputReplayer.CurrentFrame;
            int totalFrames = InputReplayer.TotalFrames;
            InputReplayer.StopReplay();

            return new ReplayInputResponse
            {
                Success = true,
                Message = $"Replay stopped at frame {stoppedFrame}/{totalFrames}",
                Action = ReplayInputAction.Stop.ToString(),
                CurrentFrame = stoppedFrame,
                TotalFrames = totalFrames,
                IsReplaying = false
            };
        }

        private static ReplayInputResponse ExecuteStatus()
        {
            if (!InputReplayer.IsReplaying)
            {
                return new ReplayInputResponse
                {
                    Success = true,
                    Message = "Not replaying.",
                    Action = ReplayInputAction.Status.ToString(),
                    IsReplaying = false
                };
            }

            return new ReplayInputResponse
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
