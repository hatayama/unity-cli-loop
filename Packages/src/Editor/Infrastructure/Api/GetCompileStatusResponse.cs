using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Compile status payload returned by the internal CLI polling bridge command.
    /// </summary>
    public class GetCompileStatusResponse : UnityCliLoopToolResponse
    {
        public bool Ready { get; set; }
        public bool HasResult { get; set; }
        public bool IsCompiling { get; set; }
        public bool IsUpdating { get; set; }
        public bool IsDomainReloadInProgress { get; set; }

        /// <summary>
        /// Main-thread-ticking seconds the active compile request has been accepted by Unity
        /// (isCompiling true) without compilationStarted firing. Zero when no request is in flight
        /// or the compile has started. The CLI brings the Editor to the front once this grows past
        /// its threshold.
        /// </summary>
        public double CompileStartPendingSeconds { get; set; }

        public JToken Result { get; set; }
        public string Message { get; set; }
    }
}
