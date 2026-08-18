using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Serves CLI-only pause point status and cleanup requests outside the normal tool slot.
    /// </summary>
    internal static class PausePointStatusBridgeCommand
    {
        private const string IdParamName = "Id";
        private const string MinimumRemainingSecondsParamName = "MinimumRemainingSeconds";

        public static PausePointStatusResponse Execute(JToken paramsToken)
        {
            string id = ReadId(paramsToken);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            return PausePointStatusResponse.FromSnapshot(snapshot);
        }

        // Called once when await-pause-point starts waiting, so a marker enabled well before a
        // slow multi-step CLI round trip does not expire before the await itself observes a hit.
        public static PausePointStatusResponse Extend(JToken paramsToken)
        {
            string id = ReadId(paramsToken);
            int minimumRemainingSeconds = ReadMinimumRemainingSeconds(paramsToken);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.ExtendExpiryForAwait(id, minimumRemainingSeconds);
            return PausePointStatusResponse.FromSnapshot(snapshot);
        }

        public static PausePointStatusResponse Clear(JToken paramsToken)
        {
            string id = ReadId(paramsToken);
            // Registry.Clear unpatches any source pause point via the hook
            // SourcePausePointPatcher wires into it, so this bridge - which must not reference
            // that Editor-only tool assembly directly - never leaves a Harmony injection attached
            // after the marker itself reports Cleared.
            // The CLI polling bridge only reports marker status; the resumed-from-pause side
            // effect is surfaced through the clear-pause-point tool response, not this path.
            (UloopPausePointSnapshot snapshot, bool _) = UloopPausePointRegistry.Clear(id);
            LogCleared(id, snapshot.StatusBeforeClear);
            if (snapshot.StatusBeforeClear == UloopPausePointStatus.Expired)
            {
                LogExpired(id, snapshot.ElapsedSinceEnabledMilliseconds);
            }

            return PausePointStatusResponse.FromSnapshot(snapshot);
        }

        // Why: PausePointTools.LogCleared duplicates this instead of sharing it, since this
        // bridge must not reference that Editor-only tool assembly. Keep both in sync if the
        // log shape or wording changes.
        private static void LogCleared(string target, string statusBeforeClear)
        {
            VibeLogger.LogInfo(
                "pause_point_cleared",
                $"Pause point cleared: {target}",
                new { Target = target, StatusBeforeClear = statusBeforeClear });
        }

        // Why: PausePointTools.LogExpired duplicates this instead of sharing it, since this
        // bridge must not reference that Editor-only tool assembly. Keep both in sync if the
        // log shape or wording changes. The physics-callback dispatch diagnostics
        // (pause_point_physics_dispatch_diagnostics / pause_point_cleared_without_hit_physics)
        // are NOT duplicated here -- they are tool-side only, since this bridge has no access to
        // the declaring-type/patch state PausePointUseCase tracks for that purpose.
        private static void LogExpired(string id, long elapsedSinceEnabledMilliseconds)
        {
            VibeLogger.LogInfo(
                "pause_point_expired",
                $"Pause point expired before being cleared: {id}",
                new { Id = id, ElapsedSinceEnabledMilliseconds = elapsedSinceEnabledMilliseconds });
        }

        private static string ReadId(JToken paramsToken)
        {
            if (paramsToken is not JObject paramsObject)
            {
                return string.Empty;
            }

            JToken idToken = paramsObject.GetValue(IdParamName, StringComparison.OrdinalIgnoreCase);
            return idToken?.ToString() ?? string.Empty;
        }

        private static int ReadMinimumRemainingSeconds(JToken paramsToken)
        {
            if (paramsToken is not JObject paramsObject)
            {
                return 0;
            }

            JToken valueToken = paramsObject.GetValue(MinimumRemainingSecondsParamName, StringComparison.OrdinalIgnoreCase);
            return valueToken?.ToObject<int>() ?? 0;
        }
    }

    /// <summary>
    /// Pause point status payload returned by the internal CLI polling bridge command.
    /// </summary>
    public class PausePointStatusResponse : UnityCliLoopToolResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public string Mode { get; set; } = string.Empty;
        public int MaxHistory { get; set; }
        public int MaxPreviewElements { get; set; }
        public IReadOnlyList<PausePointStatusCapturedHistoryFrame> CapturedVariableHistory { get; set; } =
            Array.Empty<PausePointStatusCapturedHistoryFrame>();
        public int HistoryDroppedCount { get; set; }
        public bool Expired { get; set; }
        public string EnabledAtUtc { get; set; } = string.Empty;
        public long ElapsedSinceEnabledMilliseconds { get; set; }
        public long RemainingMilliseconds { get; set; }
        public int Generation { get; set; }
        public PausePointStatusEditorState EditorState { get; set; } = new();
        public string FirstHitAtUtc { get; set; } = string.Empty;
        public string LastHitAtUtc { get; set; } = string.Empty;
        public int FirstHitSequence { get; set; }
        public int LastHitSequence { get; set; }
        public string Message { get; set; } = string.Empty;
        public string RecommendedNextAction { get; set; } = string.Empty;
        public IReadOnlyList<PausePointStatusCapturedVariable> CapturedVariables { get; set; } =
            Array.Empty<PausePointStatusCapturedVariable>();
        public bool CapturedVariablesTruncated { get; set; }
        public IReadOnlyList<string> TruncatedVariableNames { get; set; } = Array.Empty<string>();
        public int TruncatedVariableCount { get; set; }
        public string ClearedReason { get; set; } = string.Empty;
        public string StatusBeforeClear { get; set; } = string.Empty;
        public bool LateHitDiscardedAfterClear { get; set; }
        public bool SuppressedByHotReload { get; set; }
        public bool RetargetedToHotReloadPatch { get; set; }
        // Null when unset so the status contract omits the field (matches Go omitempty).
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string SuppressedByHotReloadReason { get; set; }
        // Null when unset so the status contract omits Warning (matches Go omitempty).
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Warning { get; set; }
        // Why DefaultValue/Null ignore: match Go omitempty so unresolved markers omit the fields
        // from the status contract shape (0 / empty must not appear in the shared JSON fixture).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ResolvedLine { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ResolvedLineText { get; set; }

        internal static PausePointStatusResponse FromSnapshot(UloopPausePointSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointStatusResponse
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
                    .Select(PausePointStatusCapturedHistoryFrame.FromSnapshot)
                    .ToList(),
                HistoryDroppedCount = snapshot.HistoryDroppedCount,
                Expired = snapshot.Expired,
                EnabledAtUtc = snapshot.EnabledAtUtc,
                ElapsedSinceEnabledMilliseconds = snapshot.ElapsedSinceEnabledMilliseconds,
                RemainingMilliseconds = snapshot.RemainingMilliseconds,
                Generation = snapshot.Generation,
                EditorState = PausePointStatusEditorState.FromSnapshot(snapshot.EditorState),
                FirstHitAtUtc = snapshot.FirstHitAtUtc,
                LastHitAtUtc = snapshot.LastHitAtUtc,
                FirstHitSequence = snapshot.FirstHitSequence,
                LastHitSequence = snapshot.LastHitSequence,
                Message = snapshot.Message,
                RecommendedNextAction = ResolveExpiredRecommendedNextAction(
                    snapshot.Status,
                    snapshot.RecommendedNextAction),
                CapturedVariables = snapshot.CapturedVariables
                    .Select(PausePointStatusCapturedVariable.FromCapturedVariable)
                    .ToList(),
                CapturedVariablesTruncated = snapshot.CapturedVariablesTruncated,
                TruncatedVariableNames = snapshot.TruncatedVariableNames,
                TruncatedVariableCount = snapshot.TruncatedVariableCount,
                ClearedReason = snapshot.ClearedReason,
                StatusBeforeClear = snapshot.StatusBeforeClear,
                LateHitDiscardedAfterClear = snapshot.LateHitDiscardedAfterClear,
                SuppressedByHotReload = snapshot.SuppressedByHotReload,
                RetargetedToHotReloadPatch = snapshot.RetargetedToHotReloadPatch,
                SuppressedByHotReloadReason = snapshot.SuppressedByHotReloadReason,
                // Why reason as Warning: agents already read Warning; suppressed=false clears both.
                Warning = snapshot.SuppressedByHotReload ? snapshot.SuppressedByHotReloadReason : null,
                ResolvedLine = snapshot.ResolvedLine,
                ResolvedLineText = string.IsNullOrEmpty(snapshot.ResolvedLineText)
                    ? null
                    : snapshot.ResolvedLineText
            };
        }

        // Why duplicate: this bridge must not reference the Editor-only PausePoint assembly.
        // Keep in sync with SourcePausePointConstants.ExpiredRecommendedNextAction.
        private const string ExpiredRecommendedNextAction =
            "The pause point expired before it was hit. Re-enable it, and pass --timeout-seconds with a value larger than the default 30 if you need more setup time before triggering.";

        private static string ResolveExpiredRecommendedNextAction(string status, string recommendedNextAction)
        {
            if (status == UloopPausePointStatus.Expired
                && string.IsNullOrEmpty(recommendedNextAction))
            {
                return ExpiredRecommendedNextAction;
            }

            return recommendedNextAction ?? string.Empty;
        }
    }

    /// <summary>
    /// One formatted capture frame included in the CLI-only pause point status response history.
    /// </summary>
    public class PausePointStatusCapturedHistoryFrame
    {
        public int HitSequence { get; set; }
        public int FrameCount { get; set; }
        public string HitAtUtc { get; set; } = string.Empty;
        public IReadOnlyList<PausePointStatusCapturedVariable> CapturedVariables { get; set; } =
            Array.Empty<PausePointStatusCapturedVariable>();
        public bool Truncated { get; set; }

        internal static PausePointStatusCapturedHistoryFrame FromSnapshot(
            UloopPausePointCapturedHistoryFrame snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointStatusCapturedHistoryFrame
            {
                HitSequence = snapshot.HitSequence,
                FrameCount = snapshot.FrameCount,
                HitAtUtc = snapshot.HitAtUtc,
                CapturedVariables = snapshot.CapturedVariables
                    .Select(PausePointStatusCapturedVariable.FromCapturedVariable)
                    .ToList(),
                Truncated = snapshot.Truncated
            };
        }
    }

    /// <summary>
    /// Unity Editor play state attached to pause point status evidence.
    /// </summary>
    public class PausePointStatusEditorState
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string CapturedAt { get; set; } = string.Empty;

        internal static PausePointStatusEditorState FromSnapshot(UloopPausePointEditorStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointStatusEditorState
            {
                IsPlaying = snapshot.IsPlaying,
                IsPaused = snapshot.IsPaused,
                CapturedAt = snapshot.CapturedAt
            };
        }
    }

    /// <summary>
    /// One variable captured at a source pause point, mirroring Runtime.UloopCapturedVariable's
    /// fields for the CLI polling bridge response.
    /// </summary>
    public class PausePointStatusCapturedVariable
    {
        public string Name { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string UnityObjectKind { get; set; } = string.Empty;
        public string UnityObjectPath { get; set; } = string.Empty;
        public int UnityObjectInstanceId { get; set; }
        public bool Truncated { get; set; }

        internal static PausePointStatusCapturedVariable FromCapturedVariable(UloopCapturedVariable capturedVariable)
        {
            if (capturedVariable == null)
            {
                throw new ArgumentNullException(nameof(capturedVariable));
            }

            return new PausePointStatusCapturedVariable
            {
                Name = capturedVariable.Name,
                Scope = capturedVariable.Scope,
                TypeName = capturedVariable.TypeName,
                Value = capturedVariable.Value,
                UnityObjectKind = capturedVariable.UnityObjectKind,
                UnityObjectPath = capturedVariable.UnityObjectPath,
                UnityObjectInstanceId = capturedVariable.UnityObjectInstanceId,
                Truncated = capturedVariable.Truncated
            };
        }
    }
}
