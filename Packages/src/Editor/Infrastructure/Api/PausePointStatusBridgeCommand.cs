using System;
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
                RecommendedNextAction = snapshot.RecommendedNextAction
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
}
