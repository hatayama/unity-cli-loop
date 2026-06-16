using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides the shared JSON-RPC response serializer settings used by Unity-facing command results.
    /// </summary>
    public static class JsonRpcResponseSerializer
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    // Why: response DTO property names are the public contract, but user-supplied
                    // dictionary keys can be data and must not be rewritten by the transport layer.
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = false
                }
            },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH
        };
    }
}
