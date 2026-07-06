using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Single source for the JSON shape shared by JSON-RPC responses and stored compile results as the Go CLI wire contract.
    /// </summary>
    public static class UnityCliLoopJsonResponseSerializerSettings
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH
        };
    }
}
