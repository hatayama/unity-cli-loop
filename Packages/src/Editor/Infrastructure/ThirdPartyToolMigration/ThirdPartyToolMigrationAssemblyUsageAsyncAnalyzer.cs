using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds assembly usage facts for asynchronous migration planning.
    /// </summary>
    internal static class ThirdPartyToolMigrationAssemblyUsageAsyncAnalyzer
    {
        internal static async Task<MigrationAssemblyUsage> FindMigrationAssemblyUsageAsync(
            string projectRoot,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            MigrationProgressCounter progressCounter,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");
            Debug.Assert(progressCounter != null, "progressCounter must not be null");

            List<string> asmdefDirectories = asmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories =
                await CreateAssemblyReferenceDirectoriesAsync(
                    asmdefFilePaths,
                    asmrefFilePaths,
                    sourceFileCache,
                    progressCounter,
                    ct);
            HashSet<string> legacyAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedLegacyDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentToolContractsDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentDomainDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentApplicationDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedLegacyToolInfoAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedCurrentApplicationAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedCurrentDomainAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory =
                new(StringComparer.Ordinal);
            HashSet<string> registrarAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainMetadataAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> toolContractsReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories = new(StringComparer.Ordinal);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return CreateMigrationAssemblyUsage(
                        asmdefDirectories,
                        assemblyReferenceDirectories,
                        legacyAssemblyDirectories,
                        assemblyScopedLegacyDirectories,
                        assemblyScopedCurrentToolContractsDirectories,
                        assemblyScopedCurrentApplicationDirectories,
                        assemblyScopedCurrentDomainDirectories,
                        assemblyScopedCurrentFirstPartyToolsDirectories,
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        assemblyScopedCurrentApplicationAliasesByDirectory,
                        assemblyScopedCurrentDomainAliasesByDirectory,
                        assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                        assemblyDeclaredTypeNamesByDirectory,
                        toolContractsReferenceAssemblyDirectories,
                        applicationReferenceAssemblyDirectories,
                        domainReferenceAssemblyDirectories,
                        firstPartyScreenshotReferenceAssemblyDirectories);
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                AddAssemblyScopedNames(
                    assemblyDeclaredTypeNamesByDirectory,
                    assemblyDirectory,
                    ThirdPartyToolMigrationRules.GetDeclaredTypeNames(source));
                if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
                {
                    legacyAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalUsing(source))
                {
                    assemblyScopedLegacyDirectories.Add(assemblyDirectory);
                    AddAssemblyScopedLegacyAliases(
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetLegacyGlobalNamespaceAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalToolInfoTypeAlias(source))
                {
                    AddAssemblyScopedLegacyAliases(
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetLegacyGlobalToolInfoTypeAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainGlobalUsing(source))
                {
                    assemblyScopedCurrentDomainDirectories.Add(assemblyDirectory);
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainNamespaceAlias(source))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                    AddAssemblyScopedNames(
                        assemblyScopedCurrentDomainAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetCurrentDomainGlobalNamespaceAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsGlobalUsing(source))
                {
                    assemblyScopedCurrentToolContractsDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentApplicationGlobalUsing(source))
                {
                    assemblyScopedCurrentApplicationDirectories.Add(assemblyDirectory);
                    applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentApplicationUsing(source))
                {
                    applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentApplicationNamespaceAlias(source))
                {
                    applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
                    AddAssemblyScopedNames(
                        assemblyScopedCurrentApplicationAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetCurrentApplicationGlobalNamespaceAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsGlobalUsing(source))
                {
                    assemblyScopedCurrentFirstPartyToolsDirectories.Add(assemblyDirectory);
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsNamespaceAlias(source))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                    AddAssemblyScopedNames(
                        assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetCurrentFirstPartyToolsGlobalNamespaceAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentRegistrarApi(source))
                {
                    registrarAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApi(source))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainMetadataApi(source))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApi(source))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return CreateMigrationAssemblyUsage(
                        asmdefDirectories,
                        assemblyReferenceDirectories,
                        legacyAssemblyDirectories,
                        assemblyScopedLegacyDirectories,
                        assemblyScopedCurrentToolContractsDirectories,
                        assemblyScopedCurrentApplicationDirectories,
                        assemblyScopedCurrentDomainDirectories,
                        assemblyScopedCurrentFirstPartyToolsDirectories,
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        assemblyScopedCurrentApplicationAliasesByDirectory,
                        assemblyScopedCurrentDomainAliasesByDirectory,
                        assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                        assemblyDeclaredTypeNamesByDirectory,
                        toolContractsReferenceAssemblyDirectories,
                        applicationReferenceAssemblyDirectories,
                        domainReferenceAssemblyDirectories,
                        firstPartyScreenshotReferenceAssemblyDirectories);
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                string[] legacyAssemblyAliases = Array.Empty<string>();
                if (assemblyScopedLegacyAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out HashSet<string> legacyAssemblyAliasSet))
                {
                    legacyAssemblyAliases = legacyAssemblyAliasSet
                        .OrderBy(alias => alias, StringComparer.Ordinal)
                        .ToArray();
                }
                bool hasLegacyCSharpApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);
                string[] currentApplicationAssemblyAliases =
                    GetAssemblyScopedNames(assemblyScopedCurrentApplicationAliasesByDirectory, assemblyDirectory);
                bool hasCurrentApplicationSourceTarget =
                    ThirdPartyToolMigrationRules.ContainsCurrentApplicationApiForAssembly(
                        source,
                        assemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                        currentApplicationAssemblyAliases,
                        GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory));

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainHelperApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases) ||
                    ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                        hasLegacyCSharpApi ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                        assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory) ||
                        assemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases,
                        currentApplicationAssemblyAliases,
                        GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory)) ||
                    hasCurrentApplicationSourceTarget)
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                string[] currentDomainAssemblyAliases =
                    GetAssemblyScopedNames(assemblyScopedCurrentDomainAliasesByDirectory, assemblyDirectory);
                string[] currentDomainNamespaceAliases =
                    ThirdPartyToolMigrationAliasRules.GetCombinedCurrentDomainNamespaceAliases(
                        source,
                        currentDomainAssemblyAliases);
                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                        source,
                        assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory)) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentDomainContractAliasReference(
                        source,
                        currentDomainNamespaceAliases))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                bool hasAssemblyScopedCurrentToolContractsUsing =
                    assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory);
                bool hasAssemblyScopedCurrentFirstPartyToolsUsing =
                    assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory);
                string[] assemblyDeclaredTypeNames =
                    GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory);
                string[] currentFirstPartyToolsAssemblyAliases =
                    GetAssemblyScopedNames(
                        assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                        assemblyDirectory);
                bool hasLegacyEditorWindowCaptureUtilitySourceTarget =
                    ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) || hasLegacyCSharpApi,
                        hasAssemblyScopedCurrentToolContractsUsing,
                        hasAssemblyScopedCurrentFirstPartyToolsUsing,
                        legacyAssemblyAliases,
                        currentFirstPartyToolsAssemblyAliases,
                        assemblyDeclaredTypeNames);
                bool hasCurrentFirstPartyToolsContractSourceTarget =
                    ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
                        source,
                        hasAssemblyScopedCurrentFirstPartyToolsUsing,
                        currentFirstPartyToolsAssemblyAliases,
                        assemblyDeclaredTypeNames);
                if (ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                        hasLegacyCSharpApi ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                        hasAssemblyScopedCurrentToolContractsUsing,
                        legacyAssemblyAliases,
                        assemblyDeclaredTypeNames) ||
                    hasLegacyEditorWindowCaptureUtilitySourceTarget ||
                    hasCurrentFirstPartyToolsContractSourceTarget)
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                        source,
                        hasAssemblyScopedCurrentFirstPartyToolsUsing,
                        currentFirstPartyToolsAssemblyAliases,
                        assemblyDeclaredTypeNames))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) || hasLegacyCSharpApi,
                        hasAssemblyScopedCurrentToolContractsUsing,
                        hasAssemblyScopedCurrentFirstPartyToolsUsing,
                        legacyAssemblyAliases,
                        currentFirstPartyToolsAssemblyAliases,
                        assemblyDeclaredTypeNames))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string registrarAssemblyDirectory in registrarAssemblyDirectories)
            {
                applicationReferenceAssemblyDirectories.Add(registrarAssemblyDirectory);
            }

            return CreateMigrationAssemblyUsage(
                asmdefDirectories,
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentApplicationDirectories,
                assemblyScopedCurrentDomainDirectories,
                assemblyScopedCurrentFirstPartyToolsDirectories,
                assemblyScopedLegacyAliasesByDirectory,
                assemblyScopedLegacyToolInfoAliasesByDirectory,
                assemblyScopedCurrentApplicationAliasesByDirectory,
                assemblyScopedCurrentDomainAliasesByDirectory,
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                assemblyDeclaredTypeNamesByDirectory,
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);
        }
    }
}
