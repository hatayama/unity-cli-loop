using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApiDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationAsmdefRules
    {
        internal static string[] GetMigratedAsmdefReferences(
            string reference,
            bool hasLegacyCSharpSource,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference)
        {
            if (string.Equals(reference, LegacyEditorAssemblyName, StringComparison.Ordinal))
            {
                return GetMigratedLegacyEditorReferences(
                    requiresApplicationReference,
                    requiresDomainReference,
                    requiresFirstPartyScreenshotReference);
            }

            if (string.Equals(reference, LegacyRuntimeAssemblyName, StringComparison.Ordinal))
            {
                return new[] { CurrentRuntimeGuidReference };
            }

            if (hasLegacyCSharpSource &&
                string.Equals(reference, LegacyEditorAssemblyGuidReference, StringComparison.Ordinal))
            {
                return GetMigratedLegacyEditorReferences(
                    requiresApplicationReference,
                    requiresDomainReference,
                    requiresFirstPartyScreenshotReference);
            }

            return new[] { reference };
        }

        internal static string[] GetMigratedLegacyEditorReferences(
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference)
        {
            List<string> references = new()
            {
                CurrentToolContractsGuidReference
            };
            AddRequiredMigratedLegacyEditorReference(
                references,
                requiresApplicationReference,
                CurrentApplicationGuidReference);
            AddRequiredMigratedLegacyEditorReference(
                references,
                requiresDomainReference,
                CurrentDomainGuidReference);
            AddRequiredMigratedLegacyEditorReference(
                references,
                requiresFirstPartyScreenshotReference,
                CurrentFirstPartyToolsScreenshotGuidReference);

            return references.ToArray();
        }

        internal static void AddRequiredMigratedLegacyEditorReference(
            List<string> references,
            bool isRequired,
            string reference)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");

            if (!isRequired)
            {
                return;
            }

            references.Add(reference);
        }

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

        internal static string GetCurrentAsmdefReferenceKey(string reference)
        {
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentRuntimeAssemblyName,
                    CurrentRuntimeGuidReference))
            {
                return CurrentRuntimeAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentToolContractsAssemblyName,
                    CurrentToolContractsGuidReference))
            {
                return CurrentToolContractsAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentApplicationAssemblyName,
                    CurrentApplicationGuidReference))
            {
                return CurrentApplicationAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentDomainAssemblyName,
                    CurrentDomainGuidReference))
            {
                return CurrentDomainAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentFirstPartyToolsScreenshotAssemblyName,
                    CurrentFirstPartyToolsScreenshotGuidReference))
            {
                return CurrentFirstPartyToolsScreenshotAssemblyName;
            }

            return reference;
        }

        internal static bool IsCurrentAsmdefReference(
            string reference,
            string assemblyName,
            string guidReference)
        {
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(guidReference), "guidReference must not be null or empty");

            return string.Equals(reference, assemblyName, StringComparison.Ordinal) ||
                string.Equals(reference, guidReference, StringComparison.Ordinal);
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
