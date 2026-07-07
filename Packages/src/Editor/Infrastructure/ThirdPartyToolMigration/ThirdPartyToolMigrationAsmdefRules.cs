using System;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingSignature;
using ThirdPartyToolMigrationAsmdefReferenceMigrationResult = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAsmdefReferenceMigrationResult;
using ThirdPartyToolMigrationContentResult = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationContentResult;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAsmdefReferenceRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationAsmdefRules
    {
        internal static ThirdPartyToolMigrationContentResult MigrateAsmdefSource(
            string source,
            bool hasLegacyCSharpSource,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference)
        {
            Debug.Assert(source != null, "source must not be null");

            JObject asmdef = JObject.Parse(source);
            JToken referencesToken = asmdef["references"];
            JArray references = referencesToken == null ? new JArray() : referencesToken as JArray;
            if (referencesToken != null && references == null)
            {
                return new ThirdPartyToolMigrationContentResult(
                    source,
                    0,
                    Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
            }

            string[] referenceValues = ReadReferenceValues(references);
            ThirdPartyToolMigrationAsmdefReferenceMigrationResult migrationResult = MigrateAsmdefReferences(
                referenceValues,
                hasLegacyCSharpSource,
                requiresToolContractsReference,
                requiresApplicationReference,
                requiresDomainReference,
                requiresFirstPartyScreenshotReference);

            if (!migrationResult.Changed)
            {
                return new ThirdPartyToolMigrationContentResult(
                    source,
                    0,
                    Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
            }

            JArray migratedReferences = CreateReferencesArray(migrationResult.References);
            asmdef["references"] = migratedReferences;
            return new ThirdPartyToolMigrationContentResult(
                asmdef.ToString(Formatting.Indented),
                migrationResult.ReplacementCount,
                Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
        }

        private static string[] ReadReferenceValues(JArray references)
        {
            Debug.Assert(references != null, "references must not be null");

            string[] referenceValues = new string[references.Count];
            for (int index = 0; index < references.Count; index++)
            {
                referenceValues[index] = references[index]?.Value<string>() ?? string.Empty;
            }

            return referenceValues;
        }

        private static JArray CreateReferencesArray(string[] references)
        {
            Debug.Assert(references != null, "references must not be null");

            JArray referencesArray = new();
            foreach (string reference in references)
            {
                referencesArray.Add(reference);
            }

            return referencesArray;
        }
    }
}
