using System;
using System.Collections.Generic;
using System.Diagnostics;

using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Resolves asmdef reference migration policy without JSON mutation concerns.
    /// </summary>
    public static class ThirdPartyToolMigrationAsmdefReferenceRules
    {
        public static string[] GetMigratedAsmdefReferences(
            string reference,
            bool hasLegacyCSharpSource,
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

        /// <summary>
        /// Resolves the final asmdef reference list without JSON mutation concerns.
        /// </summary>
        public static ThirdPartyToolMigrationAsmdefReferenceMigrationResult MigrateAsmdefReferences(
            string[] references,
            bool hasLegacyCSharpSource,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference)
        {
            Debug.Assert(references != null, "references must not be null");

            string[] sourceReferences = references ?? throw new ArgumentNullException(nameof(references));
            int replacementCount = 0;
            HashSet<string> addedReferences = new(StringComparer.Ordinal);
            List<string> migratedReferences = new();
            foreach (string sourceReference in sourceReferences)
            {
                string reference = sourceReference ?? string.Empty;
                string[] migratedReferenceItems = GetMigratedAsmdefReferences(
                    reference,
                    hasLegacyCSharpSource,
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

            replacementCount += AppendRequiredCurrentAsmdefReferences(
                migratedReferences,
                addedReferences,
                hasLegacyCSharpSource || requiresToolContractsReference,
                requiresApplicationReference,
                requiresDomainReference,
                requiresFirstPartyScreenshotReference);

            string[] resultReferences = replacementCount == 0
                ? sourceReferences
                : migratedReferences.ToArray();
            return new ThirdPartyToolMigrationAsmdefReferenceMigrationResult(
                resultReferences,
                replacementCount);
        }

        private static string[] GetMigratedLegacyEditorReferences(
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

        private static void AddRequiredMigratedLegacyEditorReference(
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

        private static int AppendRequiredCurrentAsmdefReferences(
            List<string> references,
            HashSet<string> addedReferences,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(addedReferences != null, "addedReferences must not be null");

            int replacementCount = 0;
            if (requiresToolContractsReference)
            {
                replacementCount += AppendRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentToolContractsGuidReference);
            }

            if (requiresApplicationReference)
            {
                replacementCount += AppendRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentApplicationGuidReference);
            }

            if (requiresDomainReference)
            {
                replacementCount += AppendRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentDomainGuidReference);
            }

            if (requiresFirstPartyScreenshotReference)
            {
                replacementCount += AppendRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentFirstPartyToolsScreenshotGuidReference);
            }

            return replacementCount;
        }

        private static int AppendRequiredCurrentAsmdefReference(
            List<string> references,
            HashSet<string> addedReferences,
            string reference)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(addedReferences != null, "addedReferences must not be null");
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");

            string referenceKey = GetCurrentAsmdefReferenceKey(reference);
            if (!addedReferences.Add(referenceKey))
            {
                return 0;
            }

            references.Add(reference);
            return 1;
        }

        public static string GetCurrentAsmdefReferenceKey(string reference)
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

        private static bool IsCurrentAsmdefReference(
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
    }
}
