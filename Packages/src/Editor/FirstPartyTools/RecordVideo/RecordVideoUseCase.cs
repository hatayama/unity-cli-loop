using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes Start, Stop, and Status for the record-video tool.
    /// </summary>
    public sealed class RecordVideoUseCase
    {
        public Task<RecordVideoResponse> ExecuteAsync(RecordVideoSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool isLinux = Application.platform == RuntimePlatform.LinuxEditor;
            if (parameters.Action == RecordVideoAction.Stop)
            {
                return Task.FromResult(ExecuteStop());
            }

            if (parameters.Action == RecordVideoAction.Status)
            {
                return Task.FromResult(ExecuteStatus());
            }

            return Task.FromResult(ExecuteStart(parameters, isLinux));
        }

        private static RecordVideoResponse ExecuteStart(RecordVideoSchema parameters, bool isLinux)
        {
            PlayModeToolPreflightResult preflight = PlayModeToolPreflightService.RequireActive();
            if (!preflight.IsValid)
            {
                return CreateFailure(RecordVideoAction.Start, preflight.ErrorMessage);
            }

            if (RecordVideoService.IsRecording)
            {
                return CreateResponse(
                    false,
                    RecordVideoConstants.AlreadyRecordingMessage,
                    RecordVideoAction.Start,
                    RecordVideoService.GetSnapshot());
            }

            ValidationResult validation = RecordVideoParameterValidator.Validate(
                parameters.FrameRate,
                parameters.MaxDurationSeconds,
                parameters.OutputPath,
                isLinux,
                parameters.ResolutionScale);
            if (!validation.IsValid)
            {
                return CreateFailure(RecordVideoAction.Start, validation.ErrorMessage);
            }

            RenderTexture renderTexture = GameViewBridge.GetRenderTexture();
            if (renderTexture == null)
            {
                return CreateFailure(
                    RecordVideoAction.Start,
                    RecordVideoConstants.RenderTextureUnavailableMessage);
            }

            (int width, int height) size = VideoFrameSizePolicy.Resolve(
                renderTexture.width,
                renderTexture.height,
                parameters.ResolutionScale);
            int width = size.width;
            int height = size.height;
            if (width == 0 || height == 0)
            {
                return CreateFailure(RecordVideoAction.Start, RecordVideoConstants.FrameSizeTooSmallMessage);
            }

            string outputPath = RecordVideoOutputPathResolver.Resolve(
                parameters.OutputPath,
                UnityCliLoopPathResolver.GetProjectRoot(),
                DateTime.Now,
                isLinux);
            bool usedDefaultOutputPath = string.IsNullOrEmpty(parameters.OutputPath);
            VideoRecordingSnapshot snapshot = RecordVideoService.Start(
                parameters.FrameRate,
                parameters.MaxDurationSeconds,
                outputPath,
                usedDefaultOutputPath,
                width,
                height,
                parameters.ResolutionScale,
                parameters.Quality);
            return CreateResponse(
                true,
                RecordVideoConstants.StartedMessage,
                RecordVideoAction.Start,
                snapshot);
        }

        private static RecordVideoResponse ExecuteStop()
        {
            if (RecordVideoService.IsRecording)
            {
                VideoRecordingSnapshot stopped = RecordVideoService.Stop(RecordVideoConstants.StoppedByCli);
                return CreateResponse(
                    true,
                    RecordVideoConstants.StoppedMessage,
                    RecordVideoAction.Stop,
                    stopped);
            }

            LastCompletedRecording lastCompleted = LastCompletedRecordingStore.TryRead();
            if (lastCompleted.HasValue && !lastCompleted.IsReported)
            {
                LastCompletedRecordingStore.MarkReported();
                return CreateResponse(
                    true,
                    RecordVideoConstants.StoppedMessage,
                    RecordVideoAction.Stop,
                    lastCompleted.Snapshot);
            }

            return CreateFailure(RecordVideoAction.Stop, RecordVideoConstants.NoRecordingMessage);
        }

        private static RecordVideoResponse ExecuteStatus()
        {
            if (RecordVideoService.IsRecording)
            {
                return CreateResponse(
                    true,
                    RecordVideoConstants.StatusRecordingMessage,
                    RecordVideoAction.Status,
                    RecordVideoService.GetSnapshot());
            }

            LastCompletedRecording lastCompleted = LastCompletedRecordingStore.TryRead();
            if (lastCompleted.HasValue)
            {
                return CreateResponse(
                    true,
                    RecordVideoConstants.StatusIdleMessage,
                    RecordVideoAction.Status,
                    lastCompleted.Snapshot);
            }

            return new RecordVideoResponse
            {
                Success = true,
                Message = RecordVideoConstants.StatusIdleMessage,
                Action = RecordVideoAction.Status.ToString(),
                IsRecording = false
            };
        }

        private static RecordVideoResponse CreateFailure(RecordVideoAction action, string message)
        {
            return new RecordVideoResponse
            {
                Success = false,
                Message = message,
                Action = action.ToString(),
                IsRecording = RecordVideoService.IsRecording
            };
        }

        private static RecordVideoResponse CreateResponse(
            bool success,
            string message,
            RecordVideoAction action,
            VideoRecordingSnapshot snapshot)
        {
            return new RecordVideoResponse
            {
                Success = success,
                Message = message,
                Action = action.ToString(),
                IsRecording = snapshot.IsRecording,
                OutputPath = snapshot.OutputPath,
                Width = snapshot.Width,
                Height = snapshot.Height,
                FrameRate = snapshot.FrameRate,
                EncodedFrameCount = snapshot.EncodedFrameCount,
                SkippedFrameCount = snapshot.SkippedFrameCount,
                ElapsedSeconds = snapshot.ElapsedSeconds,
                StoppedBy = snapshot.StoppedBy,
                Quality = snapshot.Quality
            };
        }
    }
}
