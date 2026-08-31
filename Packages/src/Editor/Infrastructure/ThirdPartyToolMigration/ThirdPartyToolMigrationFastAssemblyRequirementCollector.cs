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
    /// Collects assembly reference requirements during fast migration-target scans.
    /// </summary>
    internal static class ThirdPartyToolMigrationFastAssemblyRequirementCollector
    {
        internal static void CollectFastAssemblyReferenceRequirements(
            string source,
            string assemblyDirectory,
            bool hasAssemblyScopedCurrentToolContractsNamespaceUsage,
            bool hasAssemblyScopedCurrentDomainNamespaceUsage,
            bool hasAssemblyScopedCurrentApplicationNamespaceUsage,
            string[] currentApplicationAssemblyAliases,
            string[] currentDomainAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(
                currentApplicationAssemblyAliases != null,
                "currentApplicationAssemblyAliases must not be null");
            Debug.Assert(
                currentDomainAssemblyAliases != null,
                "currentDomainAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                applicationReferenceAssemblyDirectories != null,
                "applicationReferenceAssemblyDirectories must not be null");
            Debug.Assert(domainReferenceAssemblyDirectories != null, "domainReferenceAssemblyDirectories must not be null");

            if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
            {
                legacyAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source))
            {
                toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            bool hasCurrentApplicationSourceTarget =
                ThirdPartyToolMigrationRules.ContainsCurrentApplicationApiForAssembly(
                    source,
                    hasAssemblyScopedCurrentApplicationNamespaceUsage,
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames);
            if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source) ||
                ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                    source,
                    legacyAssemblyDirectories.Contains(assemblyDirectory) ||
                    hasAssemblyScopedCurrentToolContractsNamespaceUsage ||
                    ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                    Array.Empty<string>(),
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                hasCurrentApplicationSourceTarget)
            {
                toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (hasCurrentApplicationSourceTarget &&
                ThirdPartyToolMigrationRules.ContainsCurrentApplicationUsing(source))
            {
                applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentRegistrarApi(source))
            {
                applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApi(source))
            {
                toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApi(source) ||
                ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                    source,
                    hasAssemblyScopedCurrentDomainNamespaceUsage) ||
                ThirdPartyToolMigrationRules.ContainsCurrentDomainContractAliasReference(
                    source,
                    ThirdPartyToolMigrationAliasRules.GetCombinedCurrentDomainNamespaceAliases(
                        source,
                        currentDomainAssemblyAliases)))
            {
                domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }
        }

        internal static async Task CollectFastAssemblyReferenceRequirementsAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentDomainDirectories,
            HashSet<string> assemblyScopedCurrentApplicationDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentApplicationAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentDomainAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(
                assemblyScopedCurrentToolContractsDirectories != null,
                "assemblyScopedCurrentToolContractsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentDomainDirectories != null,
                "assemblyScopedCurrentDomainDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationDirectories != null,
                "assemblyScopedCurrentApplicationDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationAliasesByDirectory != null,
                "assemblyScopedCurrentApplicationAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentDomainAliasesByDirectory != null,
                "assemblyScopedCurrentDomainAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyDeclaredTypeNamesByDirectory != null,
                "assemblyDeclaredTypeNamesByDirectory must not be null");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                applicationReferenceAssemblyDirectories != null,
                "applicationReferenceAssemblyDirectories must not be null");
            Debug.Assert(domainReferenceAssemblyDirectories != null, "domainReferenceAssemblyDirectories must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
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
                CollectFastAssemblyReferenceRequirements(
                    source,
                    assemblyDirectory,
                    assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                    assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory),
                    assemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                    GetAssemblyScopedNames(assemblyScopedCurrentApplicationAliasesByDirectory, assemblyDirectory),
                    GetAssemblyScopedNames(assemblyScopedCurrentDomainAliasesByDirectory, assemblyDirectory),
                    GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory),
                    legacyAssemblyDirectories,
                    toolContractsReferenceAssemblyDirectories,
                    applicationReferenceAssemblyDirectories,
                    domainReferenceAssemblyDirectories);

            }
        }

        internal static async Task CollectFastAssemblyScopedCurrentToolContractsRequirementsAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentDomainDirectories,
            HashSet<string> assemblyScopedCurrentApplicationDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentApplicationAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentDomainAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(
                assemblyScopedCurrentToolContractsDirectories != null,
                "assemblyScopedCurrentToolContractsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentDomainDirectories != null,
                "assemblyScopedCurrentDomainDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationDirectories != null,
                "assemblyScopedCurrentApplicationDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationAliasesByDirectory != null,
                "assemblyScopedCurrentApplicationAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentDomainAliasesByDirectory != null,
                "assemblyScopedCurrentDomainAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyDeclaredTypeNamesByDirectory != null,
                "assemblyDeclaredTypeNamesByDirectory must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
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
                if (!assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory))
                {
                    continue;
                }

                CollectFastAssemblyReferenceRequirements(
                    source,
                    assemblyDirectory,
                    true,
                    assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory),
                    assemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                    GetAssemblyScopedNames(assemblyScopedCurrentApplicationAliasesByDirectory, assemblyDirectory),
                    GetAssemblyScopedNames(assemblyScopedCurrentDomainAliasesByDirectory, assemblyDirectory),
                    GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory),
                    legacyAssemblyDirectories,
                    toolContractsReferenceAssemblyDirectories,
                    applicationReferenceAssemblyDirectories,
                    domainReferenceAssemblyDirectories);

            }
        }

        internal static async Task CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> assemblyScopedCurrentDomainDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(
                assemblyScopedCurrentDomainDirectories != null,
                "assemblyScopedCurrentDomainDirectories must not be null");
            Debug.Assert(domainReferenceAssemblyDirectories != null, "domainReferenceAssemblyDirectories must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
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
                if (assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(source, true))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

            }
        }

    }
}
