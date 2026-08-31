using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Performs fast source-level checks before building a complete migration plan.
    /// </summary>
    internal static class ThirdPartyToolMigrationFastSourceTargetDetector
    {
        internal static bool ContainsFastCSharpMigrationTarget(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return (ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source) &&
                ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source)) ||
                ThirdPartyToolMigrationRules.ContainsSuccessPropertyHidingUnityCliLoopToolResponse(source);
        }

        internal static async Task<bool> ContainsFastCSharpSourceMigrationTargetAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedLegacyDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyToolInfoAliasesByDirectory,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentApplicationDirectories,
            HashSet<string> assemblyScopedCurrentDomainDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentApplicationAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentDomainAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(assemblyReferenceDirectories != null, "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyDirectories != null,
                "assemblyScopedLegacyDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyAliasesByDirectory != null,
                "assemblyScopedLegacyAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedLegacyToolInfoAliasesByDirectory != null,
                "assemblyScopedLegacyToolInfoAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentToolContractsDirectories != null,
                "assemblyScopedCurrentToolContractsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationDirectories != null,
                "assemblyScopedCurrentApplicationDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentDomainDirectories != null,
                "assemblyScopedCurrentDomainDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsDirectories != null,
                "assemblyScopedCurrentFirstPartyToolsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationAliasesByDirectory != null,
                "assemblyScopedCurrentApplicationAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentDomainAliasesByDirectory != null,
                "assemblyScopedCurrentDomainAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory != null,
                "assemblyScopedCurrentFirstPartyToolsAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyDeclaredTypeNamesByDirectory != null,
                "assemblyDeclaredTypeNamesByDirectory must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = ThirdPartyToolMigrationFileAccess.ReadAllText(csharpFilePath);
                inspectedEntryCount++;
                if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }

                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                string[] assemblyDeclaredTypeNames =
                    GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory);
                string[] legacyAssemblyAliases =
                    GetAssemblyScopedNames(assemblyScopedLegacyAliasesByDirectory, assemblyDirectory);
                string[] legacyAssemblyToolInfoAliases =
                    GetAssemblyScopedNames(assemblyScopedLegacyToolInfoAliasesByDirectory, assemblyDirectory);
                bool hasLegacyAssemblySource =
                    (legacyAssemblyDirectories.Contains(assemblyDirectory) ||
                    assemblyScopedLegacyDirectories.Contains(assemblyDirectory)) &&
                    ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(
                        source,
                        legacyAssemblyAliases);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                        source,
                        hasLegacyAssemblySource,
                        assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                        assemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                        assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory),
                        assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases,
                        legacyAssemblyToolInfoAliases,
                        GetAssemblyScopedNames(assemblyScopedCurrentApplicationAliasesByDirectory, assemblyDirectory),
                        GetAssemblyScopedNames(assemblyScopedCurrentDomainAliasesByDirectory, assemblyDirectory),
                        GetAssemblyScopedNames(
                            assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                            assemblyDirectory),
                        assemblyDeclaredTypeNames);
                if (result.Changed)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsFastAsmdefMigrationTarget(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ThirdPartyToolMigrationRules.ContainsLegacyAsmdefNameReference(source);
        }
    }
}
