using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
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
        private const int WindowLayoutWaitFrames = 2;

        public Task<RecordVideoResponse> ExecuteAsync(RecordVideoSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!Enum.IsDefined(typeof(RecordVideoAction), parameters.Action))
            {
                return Task.FromResult(
                    CreateFailure(parameters.Action, RecordVideoConstants.InvalidActionMessage));
            }

            bool isLinux = Application.platform == RuntimePlatform.LinuxEditor;
            if (parameters.Action == RecordVideoAction.stop)
            {
                return Task.FromResult(ExecuteStop());
            }

            if (parameters.Action == RecordVideoAction.status)
            {
                return Task.FromResult(ExecuteStatus());
            }

            return ExecuteStartAsync(parameters, isLinux, ct);
        }

        private static async Task<RecordVideoResponse> ExecuteStartAsync(
            RecordVideoSchema parameters,
            bool isLinux,
            CancellationToken ct)
        {
            bool isWindowRecording = !string.IsNullOrEmpty(parameters.WindowName);
            // A window recording paints through the Editor loop, so it does not need Play Mode.
            if (!isWindowRecording)
            {
                PlayModeToolPreflightResult preflight = PlayModeToolPreflightService.RequireActive();
                if (!preflight.IsValid)
                {
                    return CreateFailure(RecordVideoAction.start, preflight.ErrorMessage);
                }
            }

            if (RecordVideoService.IsRecording)
            {
                return CreateResponse(
                    false,
                    RecordVideoConstants.AlreadyRecordingMessage,
                    RecordVideoAction.start,
                    RecordVideoService.GetSnapshot());
            }

            ValidationResult validation = RecordVideoParameterValidator.Validate(
                parameters.FrameRate,
                parameters.MaxDurationSeconds,
                parameters.OutputPath,
                isLinux,
                parameters.ResolutionScale,
                parameters.Quality,
                parameters.MatchMode);
            if (!validation.IsValid)
            {
                return CreateFailure(RecordVideoAction.start, validation.ErrorMessage);
            }

            RecordVideoSourceResolution source = isWindowRecording
                ? await ResolveWindowSourceAsync(parameters, ct).ConfigureAwait(false)
                : ResolvePlayModeViewSource(parameters);
            if (isWindowRecording)
            {
                // ConfigureAwait(false) above leaves the continuation off Unity's context, and
                // everything below touches Editor state.
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }

            if (source.FailureMessage != null)
            {
                return CreateFailure(RecordVideoAction.start, source.FailureMessage);
            }

            // Re-checked because the window path awaits editor frames, during which another
            // command can start its own recording.
            if (RecordVideoService.IsRecording)
            {
                return CreateResponse(
                    false,
                    RecordVideoConstants.AlreadyRecordingMessage,
                    RecordVideoAction.start,
                    RecordVideoService.GetSnapshot());
            }

            (int width, int height) size = VideoFrameSizePolicy.Resolve(
                source.SourceWidth,
                source.SourceHeight,
                parameters.ResolutionScale);
            int width = size.width;
            int height = size.height;
            if (width == 0 || height == 0)
            {
                return CreateFailure(RecordVideoAction.start, RecordVideoConstants.FrameSizeTooSmallMessage);
            }

            string outputPath = RecordVideoOutputPathResolver.Resolve(
                parameters.OutputPath,
                UnityCliLoopPathResolver.GetProjectRoot(),
                DateTime.Now,
                isLinux,
                source.FileNamePrefix);
            bool usedDefaultOutputPath = string.IsNullOrEmpty(parameters.OutputPath);
            VideoRecordingSnapshot snapshot = RecordVideoService.Start(
                parameters.FrameRate,
                parameters.MaxDurationSeconds,
                outputPath,
                usedDefaultOutputPath,
                width,
                height,
                source.FrameSource,
                !isWindowRecording,
                parameters.Quality);
            return CreateResponse(
                true,
                RecordVideoConstants.StartedMessage,
                RecordVideoAction.start,
                snapshot);
        }

        private static async Task<RecordVideoSourceResolution> ResolveWindowSourceAsync(
            RecordVideoSchema parameters,
            CancellationToken ct)
        {
            EditorWindow[] windows = EditorWindowFinder.FindWindowsByName(
                parameters.WindowName,
                parameters.MatchMode);
            if (windows.Length == 0)
            {
                return RecordVideoSourceResolution.Failure(WindowNotFoundMessage(parameters));
            }

            EditorWindow window = windows[0];
            window.ShowTab();
            // A background tab keeps its old position until the layout settles, and measuring it
            // then fixes the encoder to a size no later frame matches, which skips every frame.
            bool laidOut = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                WindowLayoutWaitFrames,
                UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                ct).ConfigureAwait(false);
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!laidOut)
            {
                return RecordVideoSourceResolution.Failure(RecordVideoConstants.WindowLayoutTimedOutMessage);
            }

            if (window == null)
            {
                return RecordVideoSourceResolution.Failure(WindowNotFoundMessage(parameters));
            }

            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            return RecordVideoSourceResolution.Success(
                new EditorWindowFrameSource(window, parameters.ResolutionScale),
                Mathf.RoundToInt(window.position.width * pixelsPerPoint),
                Mathf.RoundToInt(window.position.height * pixelsPerPoint),
                RecordVideoConstants.DefaultWindowFileNamePrefix);
        }

        private static string WindowNotFoundMessage(RecordVideoSchema parameters)
        {
            string openWindows = string.Join(", ", EditorWindowFinder.GetOpenWindowNames());
            return $"Window '{parameters.WindowName}' not found (MatchMode: {parameters.MatchMode}). Open windows: {openWindows}";
        }

        private static RecordVideoSourceResolution ResolvePlayModeViewSource(RecordVideoSchema parameters)
        {
            RenderTexture renderTexture = GameViewBridge.GetRenderTexture();
            if (renderTexture == null)
            {
                return RecordVideoSourceResolution.Failure(
                    RecordVideoConstants.RenderTextureUnavailableMessage);
            }

            return RecordVideoSourceResolution.Success(
                new PlayModeViewFrameSource(parameters.ResolutionScale),
                renderTexture.width,
                renderTexture.height,
                RecordVideoOutputPathResolver.DefaultFileNamePrefix);
        }

        private static RecordVideoResponse ExecuteStop()
        {
            if (RecordVideoService.IsRecording)
            {
                VideoRecordingSnapshot stopped = RecordVideoService.Stop(RecordVideoConstants.StoppedByCli);
                return CreateResponse(
                    true,
                    RecordVideoConstants.StoppedMessage,
                    RecordVideoAction.stop,
                    stopped);
            }

            LastCompletedRecording lastCompleted = LastCompletedRecordingStore.TryRead();
            if (lastCompleted.HasValue && !lastCompleted.IsReported)
            {
                LastCompletedRecordingStore.MarkReported();
                return CreateResponse(
                    true,
                    RecordVideoConstants.StoppedMessage,
                    RecordVideoAction.stop,
                    lastCompleted.Snapshot);
            }

            return CreateFailure(RecordVideoAction.stop, RecordVideoConstants.NoRecordingMessage);
        }

        private static RecordVideoResponse ExecuteStatus()
        {
            if (RecordVideoService.IsRecording)
            {
                return CreateResponse(
                    true,
                    RecordVideoConstants.StatusRecordingMessage,
                    RecordVideoAction.status,
                    RecordVideoService.GetSnapshot());
            }

            LastCompletedRecording lastCompleted = LastCompletedRecordingStore.TryRead();
            if (lastCompleted.HasValue)
            {
                return CreateResponse(
                    true,
                    RecordVideoConstants.StatusIdleMessage,
                    RecordVideoAction.status,
                    lastCompleted.Snapshot);
            }

            return new RecordVideoResponse
            {
                Success = true,
                Message = RecordVideoConstants.StatusIdleMessage,
                Action = RecordVideoAction.status.ToString(),
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

    /// <summary>
    /// Carries the frame source, source size, and file name prefix chosen for one recording start.
    /// </summary>
    internal readonly struct RecordVideoSourceResolution
    {
        internal IGameViewFrameSource FrameSource { get; }

        internal int SourceWidth { get; }

        internal int SourceHeight { get; }

        internal string FileNamePrefix { get; }

        internal string FailureMessage { get; }

        private RecordVideoSourceResolution(
            IGameViewFrameSource frameSource,
            int sourceWidth,
            int sourceHeight,
            string fileNamePrefix,
            string failureMessage)
        {
            FrameSource = frameSource;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            FileNamePrefix = fileNamePrefix;
            FailureMessage = failureMessage;
        }

        internal static RecordVideoSourceResolution Success(
            IGameViewFrameSource frameSource,
            int sourceWidth,
            int sourceHeight,
            string fileNamePrefix)
        {
            return new RecordVideoSourceResolution(frameSource, sourceWidth, sourceHeight, fileNamePrefix, null);
        }

        internal static RecordVideoSourceResolution Failure(string failureMessage)
        {
            return new RecordVideoSourceResolution(null, 0, 0, null, failureMessage);
        }
    }
}
