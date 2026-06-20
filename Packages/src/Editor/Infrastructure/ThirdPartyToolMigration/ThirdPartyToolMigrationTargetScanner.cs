using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefMigrationRequirementResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationFastAssemblyRequirementCollector;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationFastSourceTargetDetector;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Scans a project cheaply to decide whether migration work exists.
    /// </summary>
    internal static class ThirdPartyToolMigrationTargetScanner
    {
        internal static async Task<bool> HasMigrationTargetAsync(string projectRoot, CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            MigrationTargetPreflightResult preflightResult =
                await ThirdPartyToolMigrationPreflightScanner.FindMigrationTargetAsync(projectRoot, ct);
            if (preflightResult == MigrationTargetPreflightResult.NoTargets)
            {
                return false;
            }

            if (preflightResult == MigrationTargetPreflightResult.HasTargets)
            {
                return true;
            }

            ProjectFileInventory inventory = await ProjectFileInventory.CreateAsync(
                projectRoot,
                new Progress<ThirdPartyToolMigrationProgress>(),
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            List<string> asmdefDirectories = inventory.AsmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = inventory.AsmrefFilePaths.Count == 0
                ? new List<AssemblyReferenceDirectory>()
                : CreateAssemblyReferenceDirectories(inventory.AsmdefFilePaths, inventory.AsmrefFilePaths);
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
            Dictionary<string, HashSet<string>> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory =
                new(StringComparer.Ordinal);
            HashSet<string> toolContractsReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory =
                new(StringComparer.Ordinal);
            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = File.ReadAllText(csharpFilePath);
                if (ContainsFastCSharpMigrationTarget(source))
                {
                    return true;
                }

                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    inspectedEntryCount++;
                    if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                    {
                        await Task.Yield();
                    }

                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                string[] declaredTypeNames = ThirdPartyToolMigrationRules.GetDeclaredTypeNames(source);
                AddAssemblyScopedNames(
                    assemblyDeclaredTypeNamesByDirectory,
                    assemblyDirectory,
                    declaredTypeNames);
                if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
                {
                    legacyAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalUsing(source))
                {
                    legacyAssemblyDirectories.Add(assemblyDirectory);
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

                inspectedEntryCount++;
                if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            bool hasCSharpSourceTarget = await ContainsFastCSharpSourceMigrationTargetAsync(
                inventory.CSharpFilePaths,
                asmdefDirectories,
                assemblyReferenceDirectories,
                projectRoot,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                assemblyScopedLegacyAliasesByDirectory,
                assemblyScopedLegacyToolInfoAliasesByDirectory,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentApplicationDirectories,
                assemblyScopedCurrentFirstPartyToolsDirectories,
                assemblyScopedCurrentApplicationAliasesByDirectory,
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                assemblyDeclaredTypeNamesByDirectory,
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (hasCSharpSourceTarget)
            {
                return true;
            }

            await CollectFastAssemblyReferenceRequirementsAsync(
                inventory.CSharpFilePaths,
                asmdefDirectories,
                assemblyReferenceDirectories,
                projectRoot,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentDomainDirectories,
                assemblyScopedCurrentApplicationDirectories,
                assemblyScopedCurrentApplicationAliasesByDirectory,
                assemblyDeclaredTypeNamesByDirectory,
                legacyAssemblyDirectories,
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (assemblyScopedCurrentToolContractsDirectories.Count > 0)
            {
                await CollectFastAssemblyScopedCurrentToolContractsRequirementsAsync(
                    inventory.CSharpFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot,
                    assemblyScopedCurrentToolContractsDirectories,
                    assemblyScopedCurrentDomainDirectories,
                    assemblyScopedCurrentApplicationDirectories,
                    assemblyScopedCurrentApplicationAliasesByDirectory,
                    assemblyDeclaredTypeNamesByDirectory,
                    legacyAssemblyDirectories,
                    toolContractsReferenceAssemblyDirectories,
                    applicationReferenceAssemblyDirectories,
                    domainReferenceAssemblyDirectories,
                    ct);
                if (ct.IsCancellationRequested)
                {
                    return false;
                }
            }

            if (assemblyScopedCurrentDomainDirectories.Count > 0)
            {
                await CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
                    inventory.CSharpFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot,
                    assemblyScopedCurrentDomainDirectories,
                    domainReferenceAssemblyDirectories,
                    ct);
                if (ct.IsCancellationRequested)
                {
                    return false;
                }
            }

            bool hasFirstPartyScreenshotSourceTarget = await CollectFastFirstPartyScreenshotRequirementsAsync(
                inventory.CSharpFilePaths,
                asmdefDirectories,
                assemblyReferenceDirectories,
                projectRoot,
                legacyAssemblyDirectories,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentFirstPartyToolsDirectories,
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                assemblyDeclaredTypeNamesByDirectory,
                toolContractsReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories,
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (hasFirstPartyScreenshotSourceTarget)
            {
                return true;
            }

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = File.ReadAllText(asmdefFilePath);
                if (ContainsFastAsmdefMigrationTarget(source))
                {
                    return true;
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            if (toolContractsReferenceAssemblyDirectories.Count == 0 &&
                applicationReferenceAssemblyDirectories.Count == 0 &&
                domainReferenceAssemblyDirectories.Count == 0 &&
                firstPartyScreenshotReferenceAssemblyDirectories.Count == 0)
            {
                return false;
            }

            MigrationAssemblyUsage assemblyUsage = new(
                asmdefDirectories,
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyAliasesByDirectory),
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyToolInfoAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(assemblyScopedCurrentApplicationAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(assemblyScopedCurrentFirstPartyToolsAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(assemblyDeclaredTypeNamesByDirectory),
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                if (ContainsAsmdefMigrationTarget(asmdefFilePath, projectRoot, assemblyUsage))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
