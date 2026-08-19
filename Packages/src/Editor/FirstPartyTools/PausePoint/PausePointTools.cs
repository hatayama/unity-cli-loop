using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for enabling one pause point, either by a hand-written marker id or by
    /// resolving a source file:line to a patch location. Exactly one of "Id" or "File"+"Line"
    /// must be provided.
    /// </summary>
    public class EnablePausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public int Line { get; set; }

        public int TimeoutSeconds { get; set; } = UloopPausePointRegistry.DefaultTimeoutSeconds;

        public string Mode { get; set; } = UloopPausePointCaptureMode.SingleShot;

        public int MaxHistory { get; set; } = UloopPausePointRegistry.DefaultMaxHistory;

        public int MaxPreviewElements { get; set; } = UloopPausePointRegistry.DefaultMaxPreviewElements;

        public string Method { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parameters for clearing one or all pause point markers.
    /// </summary>
    public class ClearPausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public bool All { get; set; }
    }

    /// <summary>
    /// Response shared by pause point tool commands.
    /// </summary>
    public class PausePointResponse : UnityCliLoopToolResponse
    {
        public string Id { get; set; } = string.Empty;
        public int ResolvedLine { get; set; }
        public string ResolvedLineText { get; set; } = string.Empty;
        public string ResolvedMethod { get; set; } = string.Empty;
        public string SnapshotTiming { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public string Mode { get; set; } = string.Empty;
        public int MaxHistory { get; set; }
        public int MaxPreviewElements { get; set; }
        public IReadOnlyList<PausePointCapturedHistoryFrame> CapturedVariableHistory { get; set; } =
            Array.Empty<PausePointCapturedHistoryFrame>();
        public int HistoryDroppedCount { get; set; }
        public bool Expired { get; set; }
        public string EnabledAtUtc { get; set; } = string.Empty;
        public long ElapsedSinceEnabledMilliseconds { get; set; }
        public long RemainingMilliseconds { get; set; }
        public int Generation { get; set; }
        public PausePointEditorState EditorState { get; set; } = new();
        public string FirstHitAtUtc { get; set; } = string.Empty;
        public string LastHitAtUtc { get; set; } = string.Empty;
        public int FirstHitSequence { get; set; }
        public int LastHitSequence { get; set; }
        public int ClearedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public string RecommendedNextAction { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public string ClearedReason { get; set; } = string.Empty;
        public string StatusBeforeClear { get; set; } = string.Empty;
        public bool LateHitDiscardedAfterClear { get; set; }
        public bool SuppressedByHotReload { get; set; }
        public bool RetargetedToHotReloadPatch { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string SuppressedByHotReloadReason { get; set; }

        internal static PausePointResponse FromSnapshot(UloopPausePointSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointResponse
            {
                Id = snapshot.Id,
                Status = snapshot.Status,
                IsEnabled = snapshot.IsEnabled,
                IsHit = snapshot.IsHit,
                HitCount = snapshot.HitCount,
                TimeoutSeconds = snapshot.TimeoutSeconds,
                Mode = snapshot.Mode,
                MaxHistory = snapshot.MaxHistory,
                MaxPreviewElements = snapshot.MaxPreviewElements,
                CapturedVariableHistory = snapshot.CapturedVariableHistory
                    .Select(PausePointCapturedHistoryFrame.FromSnapshot)
                    .ToList(),
                HistoryDroppedCount = snapshot.HistoryDroppedCount,
                Expired = snapshot.Expired,
                EnabledAtUtc = snapshot.EnabledAtUtc,
                ElapsedSinceEnabledMilliseconds = snapshot.ElapsedSinceEnabledMilliseconds,
                RemainingMilliseconds = snapshot.RemainingMilliseconds,
                Generation = snapshot.Generation,
                EditorState = PausePointEditorState.FromSnapshot(snapshot.EditorState),
                FirstHitAtUtc = snapshot.FirstHitAtUtc,
                LastHitAtUtc = snapshot.LastHitAtUtc,
                FirstHitSequence = snapshot.FirstHitSequence,
                LastHitSequence = snapshot.LastHitSequence,
                Message = snapshot.Message,
                RecommendedNextAction = ResolveExpiredRecommendedNextAction(
                    snapshot.Status,
                    snapshot.RecommendedNextAction),
                ClearedReason = snapshot.ClearedReason,
                StatusBeforeClear = snapshot.StatusBeforeClear,
                LateHitDiscardedAfterClear = snapshot.LateHitDiscardedAfterClear,
                SuppressedByHotReload = snapshot.SuppressedByHotReload,
                RetargetedToHotReloadPatch = snapshot.RetargetedToHotReloadPatch,
                SuppressedByHotReloadReason = snapshot.SuppressedByHotReloadReason,
                ResolvedLine = snapshot.ResolvedLine,
                ResolvedLineText = snapshot.ResolvedLineText ?? string.Empty
            };
        }

        internal static PausePointResponse FromClearAll(UloopPausePointClearAllResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new PausePointResponse
            {
                Status = UloopPausePointStatus.Cleared,
                ClearedCount = result.ClearedCount,
                EditorState = PausePointEditorState.FromSnapshot(result.EditorState),
                Message = result.ClearedCount == 0
                    ? "No active pause points to clear."
                    : "Pause points cleared.",
                Warning = result.ResumedFromPause
                    ? SourcePausePointConstants.ClearResumedPlayModeWarning
                    : string.Empty
            };
        }

        private static string ResolveExpiredRecommendedNextAction(string status, string recommendedNextAction)
        {
            if (status == UloopPausePointStatus.Expired
                && string.IsNullOrEmpty(recommendedNextAction))
            {
                return SourcePausePointConstants.ExpiredRecommendedNextAction;
            }

            return recommendedNextAction ?? string.Empty;
        }
    }

    /// <summary>
    /// One formatted capture frame included in the enable-pause-point response history.
    /// </summary>
    public class PausePointCapturedHistoryFrame
    {
        public int HitSequence { get; set; }
        public int FrameCount { get; set; }
        public string HitAtUtc { get; set; } = string.Empty;
        public IReadOnlyList<PausePointCapturedVariable> CapturedVariables { get; set; } =
            Array.Empty<PausePointCapturedVariable>();
        public bool Truncated { get; set; }

        internal static PausePointCapturedHistoryFrame FromSnapshot(
            UloopPausePointCapturedHistoryFrame snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointCapturedHistoryFrame
            {
                HitSequence = snapshot.HitSequence,
                FrameCount = snapshot.FrameCount,
                HitAtUtc = snapshot.HitAtUtc,
                CapturedVariables = snapshot.CapturedVariables
                    .Select(PausePointCapturedVariable.FromSnapshot)
                    .ToList(),
                Truncated = snapshot.Truncated
            };
        }
    }

    /// <summary>
    /// One formatted variable included in a pause point capture history frame.
    /// </summary>
    public class PausePointCapturedVariable
    {
        public string Name { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string UnityObjectKind { get; set; } = string.Empty;
        public string UnityObjectPath { get; set; } = string.Empty;
        public int UnityObjectInstanceId { get; set; }
        public bool Truncated { get; set; }

        internal static PausePointCapturedVariable FromSnapshot(UloopCapturedVariable snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointCapturedVariable
            {
                Name = snapshot.Name,
                Scope = snapshot.Scope,
                TypeName = snapshot.TypeName,
                Value = snapshot.Value,
                UnityObjectKind = snapshot.UnityObjectKind,
                UnityObjectPath = snapshot.UnityObjectPath,
                UnityObjectInstanceId = snapshot.UnityObjectInstanceId,
                Truncated = snapshot.Truncated
            };
        }
    }

    /// <summary>
    /// Unity Editor play state attached to pause point tool evidence.
    /// </summary>
    public class PausePointEditorState
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string CapturedAt { get; set; } = string.Empty;

        internal static PausePointEditorState FromSnapshot(UloopPausePointEditorStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointEditorState
            {
                IsPlaying = snapshot.IsPlaying,
                IsPaused = snapshot.IsPaused,
                CapturedAt = snapshot.CapturedAt
            };
        }
    }

    /// <summary>
    /// Exposes pause point enabling as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class EnablePausePointTool : UnityCliLoopTool<EnablePausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(EnablePausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Enable(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Exposes pause point clearing as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class ClearPausePointTool : UnityCliLoopTool<ClearPausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(ClearPausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Clear(parameters);
            return Task.FromResult(response);
        }
    }
}
