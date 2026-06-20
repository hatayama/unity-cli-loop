using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Propagates removed PlayerLoopTiming signatures across reachable files.
    /// </summary>
    internal static class ThirdPartyToolMigrationCrossFileTimingMigrationPlanner
    {
        internal static void AddRemovedPlayerLoopTimingSignatures(
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                removedSignaturesByAssemblyDirectory,
            string assemblyDirectory,
            ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature[] removedSignatures)
        {
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(removedSignatures != null, "removedSignatures must not be null");

            if (removedSignatures.Length == 0)
            {
                return;
            }

            if (!removedSignaturesByAssemblyDirectory.TryGetValue(
                    assemblyDirectory,
                    out List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature> signatures))
            {
                signatures = new List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>();
                removedSignaturesByAssemblyDirectory.Add(assemblyDirectory, signatures);
            }

            signatures.AddRange(removedSignatures);
        }

        internal static int ApplyCrossFilePlayerLoopTimingCallerArgumentMigrations(
            List<string> csharpFilePaths,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            List<MigrationFileChange> changes,
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                removedSignaturesByAssemblyDirectory,
            Func<string, string> readAllText)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(changes != null, "changes must not be null");
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");
            Debug.Assert(readAllText != null, "readAllText must not be null");

            if (removedSignaturesByAssemblyDirectory.Count == 0)
            {
                return 0;
            }

            Dictionary<string, string[]> referencedAssemblyDirectoriesByDirectory =
                CreateReferencedAssemblyDirectoriesByDirectory(assemblyUsage.AsmdefDirectories);
            HashSet<string> asmdefDirectorySet = new(assemblyUsage.AsmdefDirectories, StringComparer.Ordinal);
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                activeRemovedSignaturesByAssemblyDirectory =
                    CloneRemovedPlayerLoopTimingSignatures(removedSignaturesByAssemblyDirectory);
            if (!HasRemovedPlayerLoopTimingSignatures(activeRemovedSignaturesByAssemblyDirectory))
            {
                return 0;
            }

            int replacementCount = 0;
            while (HasRemovedPlayerLoopTimingSignatures(activeRemovedSignaturesByAssemblyDirectory))
            {
                Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    nextRemovedSignaturesByAssemblyDirectory = new(StringComparer.Ordinal);
                foreach (string csharpFilePath in csharpFilePaths)
                {
                    string assemblyDirectory = FindNearestAssemblyDirectory(
                        csharpFilePath,
                        assemblyUsage.AsmdefDirectories,
                        assemblyUsage.AssemblyReferenceDirectories,
                        projectRoot);
                    string originalSource = readAllText(csharpFilePath);
                    string source = GetPendingMigrationFileContent(csharpFilePath, changes, originalSource);
                    string[] legacyAssemblyAliases = GetAssemblyScopedNameArray(
                        assemblyUsage.AssemblyScopedLegacyAliasesByDirectory,
                        assemblyDirectory);
                    List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature> activeRemovedSignatures =
                        asmdefDirectorySet.Contains(assemblyDirectory)
                            ? GetReachableRemovedPlayerLoopTimingSignatures(
                                assemblyDirectory,
                                activeRemovedSignaturesByAssemblyDirectory,
                                referencedAssemblyDirectoriesByDirectory)
                            : GetAllRemovedPlayerLoopTimingSignatures(
                                activeRemovedSignaturesByAssemblyDirectory);
                    if (activeRemovedSignatures.Count == 0)
                    {
                        continue;
                    }

                    ThirdPartyToolMigrationContentResult callerResult =
                        ThirdPartyToolMigrationRules.RemoveLegacyPlayerLoopTimingCallerArgumentsForLegacyAssembly(
                            source,
                            originalSource,
                            activeRemovedSignatures.ToArray(),
                            legacyAssemblyAliases);
                    if (!callerResult.Changed)
                    {
                        continue;
                    }

                    UpsertMigrationFileChange(changes, csharpFilePath, callerResult.Content);
                    replacementCount += callerResult.ReplacementCount;

                    bool canMigrateBareLegacyPlayerLoopTiming =
                        CanMigrateBareLegacyPlayerLoopTimingForAssembly(
                            assemblyUsage,
                            assemblyDirectory,
                            originalSource);
                    ThirdPartyToolMigrationContentResult parameterResult =
                        ThirdPartyToolMigrationRules.RemoveLegacyPlayerLoopTimingParametersForLegacyAssembly(
                            callerResult.Content,
                            originalSource,
                            legacyAssemblyAliases,
                            canMigrateBareLegacyPlayerLoopTiming,
                            activeRemovedSignatures
                                .Select(signature => signature.MethodName)
                                .ToArray());
                    if (!parameterResult.Changed)
                    {
                        continue;
                    }

                    UpsertMigrationFileChange(changes, csharpFilePath, parameterResult.Content);
                    replacementCount += parameterResult.ReplacementCount;
                    AddRemovedPlayerLoopTimingSignatures(
                        nextRemovedSignaturesByAssemblyDirectory,
                        assemblyDirectory,
                        parameterResult.RemovedPlayerLoopTimingSignatures);
                }

                activeRemovedSignaturesByAssemblyDirectory = nextRemovedSignaturesByAssemblyDirectory;
            }

            return replacementCount;
        }

        internal static bool CanMigrateBareLegacyPlayerLoopTimingForAssembly(
            MigrationAssemblyUsage assemblyUsage,
            string assemblyDirectory,
            string source)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(source != null, "source must not be null");

            return assemblyUsage.LegacyAssemblyDirectories.Contains(assemblyDirectory) ||
                assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                assemblyUsage.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory) ||
                assemblyUsage.AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory) ||
                ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                ThirdPartyToolMigrationRules.ContainsCurrentApplicationApi(source);
        }

        internal static bool HasRemovedPlayerLoopTimingSignatures(
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                removedSignaturesByAssemblyDirectory)
        {
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");

            foreach (
                KeyValuePair<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    signaturesByAssemblyDirectory in removedSignaturesByAssemblyDirectory)
            {
                if (signaturesByAssemblyDirectory.Value.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
            CloneRemovedPlayerLoopTimingSignatures(
                Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    removedSignaturesByAssemblyDirectory)
        {
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");

            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>> clone =
                new(StringComparer.Ordinal);
            foreach (
                KeyValuePair<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    signaturesByAssemblyDirectory in removedSignaturesByAssemblyDirectory)
            {
                clone.Add(
                    signaturesByAssemblyDirectory.Key,
                    new List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>(
                        signaturesByAssemblyDirectory.Value));
            }

            return clone;
        }

        internal static List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>
            GetAllRemovedPlayerLoopTimingSignatures(
                Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    removedSignaturesByAssemblyDirectory)
        {
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");

            List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature> signatures = new();
            foreach (
                KeyValuePair<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    signaturesByAssemblyDirectory in removedSignaturesByAssemblyDirectory)
            {
                signatures.AddRange(signaturesByAssemblyDirectory.Value);
            }

            return signatures;
        }

        internal static List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>
            GetReachableRemovedPlayerLoopTimingSignatures(
                string assemblyDirectory,
                Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                    removedSignaturesByAssemblyDirectory,
                Dictionary<string, string[]> referencedAssemblyDirectoriesByDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");
            Debug.Assert(
                referencedAssemblyDirectoriesByDirectory != null,
                "referencedAssemblyDirectoriesByDirectory must not be null");

            List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature> signatures = new();
            AddRemovedPlayerLoopTimingSignaturesForDirectory(
                signatures,
                removedSignaturesByAssemblyDirectory,
                assemblyDirectory);
            if (!referencedAssemblyDirectoriesByDirectory.TryGetValue(
                    assemblyDirectory,
                    out string[] referencedAssemblyDirectories))
            {
                return signatures;
            }

            foreach (string referencedAssemblyDirectory in referencedAssemblyDirectories)
            {
                AddRemovedPlayerLoopTimingSignaturesForDirectory(
                    signatures,
                    removedSignaturesByAssemblyDirectory,
                    referencedAssemblyDirectory);
            }

            return signatures;
        }

        internal static void AddRemovedPlayerLoopTimingSignaturesForDirectory(
            List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature> targetSignatures,
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                removedSignaturesByAssemblyDirectory,
            string assemblyDirectory)
        {
            Debug.Assert(targetSignatures != null, "targetSignatures must not be null");
            Debug.Assert(
                removedSignaturesByAssemblyDirectory != null,
                "removedSignaturesByAssemblyDirectory must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");

            if (!removedSignaturesByAssemblyDirectory.TryGetValue(
                    assemblyDirectory,
                    out List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature> signatures))
            {
                return;
            }

            targetSignatures.AddRange(signatures);
        }

        internal static string GetPendingMigrationFileContent(
            string filePath,
            List<MigrationFileChange> changes,
            string fallbackContent)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(changes != null, "changes must not be null");
            Debug.Assert(fallbackContent != null, "fallbackContent must not be null");

            foreach (MigrationFileChange change in changes)
            {
                if (string.Equals(change.FilePath, filePath, StringComparison.Ordinal))
                {
                    return change.Content;
                }
            }

            return fallbackContent;
        }

        internal static string[] GetAssemblyScopedNameArray(
            Dictionary<string, string[]> namesByDirectory,
            string assemblyDirectory)
        {
            Debug.Assert(namesByDirectory != null, "namesByDirectory must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");

            if (!namesByDirectory.TryGetValue(assemblyDirectory, out string[] names))
            {
                return Array.Empty<string>();
            }

            return names;
        }

        internal static void UpsertMigrationFileChange(
            List<MigrationFileChange> changes,
            string filePath,
            string content)
        {
            Debug.Assert(changes != null, "changes must not be null");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            for (int index = 0; index < changes.Count; index++)
            {
                MigrationFileChange change = changes[index];
                if (!string.Equals(change.FilePath, filePath, StringComparison.Ordinal))
                {
                    continue;
                }

                changes[index] = new MigrationFileChange(filePath, content);
                return;
            }

            changes.Add(new MigrationFileChange(filePath, content));
        }
    }
}
