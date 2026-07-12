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

        public ServerBusyErrorData(
            string runningToolName,
            string requestedToolName,
            bool? isPlaying,
            bool? isPaused,
            string message,
            double? secondsSinceLastMainThreadTick = null,
            bool? isCompiling = null,
            bool? isUpdating = null)
            : base(message)
        {
            this.runningToolName = runningToolName;
            this.requestedToolName = requestedToolName;
            this.isPlaying = isPlaying;
            this.isPaused = isPaused;
            this.isCompiling = isCompiling;
            this.isUpdating = isUpdating;
            this.secondsSinceLastMainThreadTick = secondsSinceLastMainThreadTick;
        }
    }
}
