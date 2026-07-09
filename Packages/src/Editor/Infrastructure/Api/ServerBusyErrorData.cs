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

        public ServerBusyErrorData(
            string runningToolName,
            string requestedToolName,
            bool? isPlaying,
            bool? isPaused,
            string message)
            : base(message)
        {
            this.runningToolName = runningToolName;
            this.requestedToolName = requestedToolName;
            this.isPlaying = isPlaying;
            this.isPaused = isPaused;
        }
    }
}
