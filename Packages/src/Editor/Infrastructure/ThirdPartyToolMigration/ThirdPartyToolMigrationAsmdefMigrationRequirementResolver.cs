using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Determines whether an asmdef needs source or reference migration.
    /// </summary>
    internal static class ThirdPartyToolMigrationAsmdefMigrationRequirementResolver
    {
        internal static bool ContainsAsmdefMigrationTarget(
            string asmdefFilePath,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            bool hasLegacyCSharpSource;
            bool requiresToolContractsReference;
            bool requiresApplicationReference;
            bool requiresDomainReference;
            bool requiresFirstPartyScreenshotReference;
            bool hasAssemblyMigrationRequirement = TryGetAsmdefMigrationRequirements(
                asmdefFilePath,
                projectRoot,
                assemblyUsage,
                out hasLegacyCSharpSource,
                out requiresToolContractsReference,
                out requiresApplicationReference,
                out requiresDomainReference,
                out requiresFirstPartyScreenshotReference);
            string source = ThirdPartyToolMigrationFileAccess.ReadAllText(asmdefFilePath);
            if (!hasAssemblyMigrationRequirement &&
                !ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source))
            {
                return false;
            }

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource,
                    requiresToolContractsReference,
                    requiresApplicationReference,
                    requiresDomainReference,
                    requiresFirstPartyScreenshotReference);

            return result.Changed;
        }

        internal static bool TryGetAsmdefMigrationRequirements(
            string asmdefFilePath,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            out bool hasLegacyCSharpSource,
            out bool requiresToolContractsReference,
            out bool requiresApplicationReference,
            out bool requiresDomainReference,
            out bool requiresFirstPartyScreenshotReference)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? projectRoot;
            hasLegacyCSharpSource = assemblyUsage.LegacyAssemblyDirectories.Contains(asmdefDirectory);
            requiresToolContractsReference =
                assemblyUsage.ToolContractsReferenceAssemblyDirectories.Contains(asmdefDirectory);
            requiresApplicationReference =
                assemblyUsage.ApplicationReferenceAssemblyDirectories.Contains(asmdefDirectory);
            requiresDomainReference =
                assemblyUsage.DomainReferenceAssemblyDirectories.Contains(asmdefDirectory);
            requiresFirstPartyScreenshotReference =
                assemblyUsage.FirstPartyScreenshotReferenceAssemblyDirectories.Contains(asmdefDirectory);
            return hasLegacyCSharpSource ||
                requiresToolContractsReference ||
                requiresApplicationReference ||
                requiresDomainReference ||
                requiresFirstPartyScreenshotReference;
        }
    }
}
