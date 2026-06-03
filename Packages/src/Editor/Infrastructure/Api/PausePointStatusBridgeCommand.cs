using System;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Serves CLI-only debug break status and cleanup requests outside the normal tool slot.
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
    /// Debug break status payload returned by the internal CLI polling bridge command.
    /// </summary>
    public class PausePointStatusResponse : UnityCliLoopToolResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string Message { get; set; } = string.Empty;

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
                ElapsedMilliseconds = snapshot.ElapsedMilliseconds,
                IsPlaying = snapshot.IsPlaying,
                IsPaused = snapshot.IsPaused,
                Message = snapshot.Message
            };
        }
    }
}
