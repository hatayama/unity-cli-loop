using System;
using System.Collections.Generic;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingSignature;
using ThirdPartyToolMigrationContentResult = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationContentResult;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAsmdefReferenceRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationAsmdefRules
    {
        internal static void AddRequiredCurrentAsmdefReferences(
            JArray references,
            HashSet<string> addedReferences,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference,
            ref int replacementCount)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(addedReferences != null, "addedReferences must not be null");

            if (requiresToolContractsReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentToolContractsGuidReference,
                    ref replacementCount);
            }

            if (requiresApplicationReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentApplicationGuidReference,
                    ref replacementCount);
            }

            if (requiresDomainReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentDomainGuidReference,
                    ref replacementCount);
            }

            if (requiresFirstPartyScreenshotReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentFirstPartyToolsScreenshotGuidReference,
                    ref replacementCount);
            }
        }

        internal static void AddRequiredCurrentAsmdefReference(
            JArray references,
            HashSet<string> addedReferences,
            string reference,
            ref int replacementCount)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(addedReferences != null, "addedReferences must not be null");
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");

            string referenceKey = GetCurrentAsmdefReferenceKey(reference);
            if (!addedReferences.Add(referenceKey))
            {
                return;
            }

            references.Add(reference);
            replacementCount++;
        }

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

            int replacementCount = 0;
            HashSet<string> addedReferences = new(StringComparer.Ordinal);
            JArray migratedReferences = new();
            foreach (JToken referenceToken in references)
            {
                string reference = referenceToken.Value<string>() ?? string.Empty;
                string[] migratedReferenceItems = GetMigratedAsmdefReferences(
                    reference,
                    hasLegacyCSharpSource,
                    requiresToolContractsReference,
                    requiresApplicationReference,
                    requiresDomainReference,
                    requiresFirstPartyScreenshotReference);
                bool referenceChanged = migratedReferenceItems.Length != 1 ||
                    !string.Equals(migratedReferenceItems[0], reference, StringComparison.Ordinal);
                if (referenceChanged)
                {
                    replacementCount++;
                }

                foreach (string migratedReference in migratedReferenceItems)
                {
                    string migratedReferenceKey = GetCurrentAsmdefReferenceKey(migratedReference);
                    if (!addedReferences.Add(migratedReferenceKey))
                    {
                        continue;
                    }

                    migratedReferences.Add(migratedReference);
                }
            }

            AddRequiredCurrentAsmdefReferences(
                migratedReferences,
                addedReferences,
                hasLegacyCSharpSource || requiresToolContractsReference,
                requiresApplicationReference,
                requiresDomainReference,
                requiresFirstPartyScreenshotReference,
                ref replacementCount);

            if (replacementCount == 0)
            {
                return new ThirdPartyToolMigrationContentResult(
                    source,
                    0,
                    Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
            }

            asmdef["references"] = migratedReferences;
            return new ThirdPartyToolMigrationContentResult(
                asmdef.ToString(Formatting.Indented),
                replacementCount,
                Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
        }

    }
}
