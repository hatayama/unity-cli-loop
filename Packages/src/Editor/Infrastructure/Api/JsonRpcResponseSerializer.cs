using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides the shared JSON-RPC response serializer settings used by Unity-facing command results.
    /// </summary>
    public static class JsonRpcResponseSerializer
    {
        public static readonly Newtonsoft.Json.JsonSerializerSettings Settings =
            UnityCliLoopJsonResponseSerializerSettings.Settings;
    }
}
