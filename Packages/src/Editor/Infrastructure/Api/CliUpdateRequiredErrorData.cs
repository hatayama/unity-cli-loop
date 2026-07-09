using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Carries protocol mismatch details plus optional CLI update instructions.
    /// </summary>
    public class CliUpdateRequiredErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.CliUpdateRequired;

        public string currentCliVersion { get; }

        public int? currentProtocolVersion { get; }

        public int requiredProtocolVersion { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string updateCommand { get; }

        public bool retryableAfterUpdate { get; }

        public CliUpdateRequiredErrorData(
            string currentCliVersion,
            int? currentProtocolVersion,
            int requiredProtocolVersion,
            string updateCommand) : base("Install matching uloop CLI and Unity package versions, then retry the original command.")
        {
            this.currentCliVersion = string.IsNullOrWhiteSpace(currentCliVersion) ? null : currentCliVersion;
            this.currentProtocolVersion = currentProtocolVersion;
            this.requiredProtocolVersion = requiredProtocolVersion;
            this.updateCommand = updateCommand;
            retryableAfterUpdate = true;
        }
    }
}
