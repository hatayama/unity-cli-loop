using System;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationDetectionRules
    {
        internal static bool ContainsLegacyAsmdefNameReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, LegacyEditorAssemblyName) &&
                !ContainsTextFragment(source, LegacyRuntimeAssemblyName))
            {
                return false;
            }

            JObject asmdef = JObject.Parse(source);
            if (asmdef["references"] is not JArray references)
            {
                return false;
            }

            foreach (JToken reference in references)
            {
                string referenceValue = reference.Value<string>() ?? string.Empty;
                if (string.Equals(referenceValue, LegacyEditorAssemblyName, StringComparison.Ordinal) ||
                    string.Equals(referenceValue, LegacyRuntimeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
