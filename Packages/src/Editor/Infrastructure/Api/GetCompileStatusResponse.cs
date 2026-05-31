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
        public JToken Result { get; set; }
        public string Message { get; set; }
    }
}
