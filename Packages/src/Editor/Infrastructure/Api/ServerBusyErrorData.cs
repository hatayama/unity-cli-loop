using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Keeps busy responses machine-readable so CLI clients can classify them as retryable.
    /// </summary>
    public class ServerBusyErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.ServerBusy;

        public string runningToolName { get; }

        public string requestedToolName { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isPlaying { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isPaused { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isCompiling { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isUpdating { get; }

        // Lets a client distinguish "main thread still ticking, tool genuinely running long" from
        // a frozen/deadlocked Editor while BUSY, without needing native stack sampling.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? secondsSinceLastMainThreadTick { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? runningToolElapsedSeconds { get; }

        // Lets a client say "waiting for the stalled main thread" instead of "running" when the
        // slot holder has not started, or has already finished, its tool code.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string runningToolPhase { get; }

        public ServerBusyErrorData(
            string runningToolName,
            string requestedToolName,
            bool? isPlaying,
            bool? isPaused,
            string message,
            double? secondsSinceLastMainThreadTick = null,
            bool? isCompiling = null,
            bool? isUpdating = null,
            int? runningToolElapsedSeconds = null,
            string runningToolPhase = null)
            : base(message)
        {
            this.runningToolName = runningToolName;
            this.requestedToolName = requestedToolName;
            this.isPlaying = isPlaying;
            this.isPaused = isPaused;
            this.isCompiling = isCompiling;
            this.isUpdating = isUpdating;
            this.secondsSinceLastMainThreadTick = secondsSinceLastMainThreadTick;
            this.runningToolElapsedSeconds = runningToolElapsedSeconds;
            this.runningToolPhase = runningToolPhase;
        }
    }
}
