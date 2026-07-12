using System;
using System.Collections.Generic;
using System.Linq;
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

        public static PausePointStatusResponse Execute(JToken paramsToken)
        {
            string id = ReadId(paramsToken);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            return PausePointStatusResponse.FromSnapshot(snapshot);
        }

        public static PausePointStatusResponse Clear(JToken paramsToken)
        {
            string id = ReadId(paramsToken);
            // Registry.Clear unpatches any source pause point via the hook
            // SourcePausePointPatcher wires into it, so this bridge - which must not reference
            // that Editor-only tool assembly directly - never leaves a Harmony injection attached
            // after the marker itself reports Cleared.
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Clear(id);
            return PausePointStatusResponse.FromSnapshot(snapshot);
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
        public string ClearedReason { get; set; } = string.Empty;
        public string StatusBeforeClear { get; set; } = string.Empty;
        public bool LateHitDiscardedAfterClear { get; set; }

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
                RecommendedNextAction = snapshot.RecommendedNextAction,
                CapturedVariables = snapshot.CapturedVariables
                    .Select(PausePointStatusCapturedVariable.FromCapturedVariable)
                    .ToList(),
                CapturedVariablesTruncated = snapshot.CapturedVariablesTruncated,
                ClearedReason = snapshot.ClearedReason,
                StatusBeforeClear = snapshot.StatusBeforeClear,
                LateHitDiscardedAfterClear = snapshot.LateHitDiscardedAfterClear
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
                UnityObjectInstanceId = capturedVariable.UnityObjectInstanceId
            };
        }
    }
}
