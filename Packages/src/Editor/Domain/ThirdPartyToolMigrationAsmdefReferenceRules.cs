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
