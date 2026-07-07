using System;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads compile-specific JSON-RPC request metadata used before tool dispatch.
    /// </summary>
    internal static class JsonRpcCompileRequestMetadataReader
    {
        private const string WaitForDomainReloadParamName = "WaitForDomainReload";

        internal static bool? ReadWaitsForDomainReload(JToken paramsToken)
        {
            if (paramsToken is not JObject paramsObject)
            {
                return null;
            }

            JToken waitForDomainReloadToken =
                paramsObject.GetValue(WaitForDomainReloadParamName, StringComparison.OrdinalIgnoreCase);
            if (waitForDomainReloadToken == null)
            {
                return null;
            }

            if (waitForDomainReloadToken.Type != JTokenType.Boolean)
            {
                return null;
            }

            return waitForDomainReloadToken.Value<bool>();
        }
    }
}
