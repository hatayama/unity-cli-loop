using System;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds the get-editor-status payload behind uloop status from the execution slot and the
    /// cached Editor state.
    /// </summary>
    internal static class EditorStatusBridgeCommand
    {
        // Why no main-thread switch and no Unity API: this must answer while the main thread is
        // blocked, so it reads only thread-safe snapshots. The stall seconds come in as a
        // parameter so tests can choose the value, and so does the process ID, which the caller reads
        // once instead of on every answer.
        internal static GetEditorStatusResponse Execute(
            UnityCliLoopToolRegistrarService toolRegistrarService,
            double secondsSinceLastMainThreadTick,
            EditorDialogInfo[] openDialogs,
            int editorProcessId)
        {
            if (toolRegistrarService == null)
            {
                throw new ArgumentNullException(nameof(toolRegistrarService));
            }

            if (openDialogs == null)
            {
                throw new ArgumentNullException(nameof(openDialogs));
            }

            if (editorProcessId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(editorProcessId), editorProcessId, "editorProcessId must name a process");
            }

            UnityCliLoopExecutionStatus executionStatus = toolRegistrarService.GetExecutionStatus();
            (bool HasValue, bool IsPlaying, bool IsPaused) playState =
                UnityCliLoopEditorStateSnapshot.GetPlayState();
            (bool HasValue, bool IsCompiling, bool IsUpdating) compileState =
                UnityCliLoopEditorStateSnapshot.GetCompileState();
            return new GetEditorStatusResponse
            {
                IsBusy = executionStatus.IsBusy,
                RunningToolName = executionStatus.RunningToolName,
                RunningToolElapsedSeconds = executionStatus.RunningToolElapsedSeconds,
                RunningToolPhase = executionStatus.RunningToolPhase,
                HasEditorState = playState.HasValue && compileState.HasValue,
                IsPlaying = playState.IsPlaying,
                IsPaused = playState.IsPaused,
                IsCompiling = compileState.IsCompiling,
                IsUpdating = compileState.IsUpdating,
                SecondsSinceLastMainThreadTick = secondsSinceLastMainThreadTick,
                OpenDialogs = openDialogs,
                ProcessId = editorProcessId
            };
        }
    }
}
