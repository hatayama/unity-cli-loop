using System;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads run-tests JSON-RPC request metadata used before tool dispatch.
    /// </summary>
    internal static class JsonRpcRunTestsRequestMetadataReader
    {
        private const string RespectEnterPlayModeSettingsParamName = "RespectEnterPlayModeSettings";
        private const string TestModeParamName = "TestMode";
        private const string PlayModeValue = "PlayMode";

        internal static bool ReadRespectsEnterPlayModeSettings(JToken paramsToken)
        {
            if (paramsToken is not JObject paramsObject)
            {
                return false;
            }

            bool? respectEnterPlayModeSettings = StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                paramsObject,
                RespectEnterPlayModeSettingsParamName,
                StringComparison.OrdinalIgnoreCase);
            if (respectEnterPlayModeSettings != true)
            {
                return false;
            }

            JToken testModeToken = paramsObject.GetValue(TestModeParamName, StringComparison.OrdinalIgnoreCase);
            string testMode = testModeToken?.ToString() ?? "";
            return string.Equals(testMode, PlayModeValue, StringComparison.OrdinalIgnoreCase);
        }
    }
}
