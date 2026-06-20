using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Streams project files for cheap fixed-text migration markers before the full target scan runs.
    /// </summary>
    internal static class ThirdPartyToolMigrationStreamingPreflightScanner
    {
        private static readonly string[] DirectCSharpCandidateMarkers =
        {
            ThirdPartyToolMigrationRuleCatalog.LegacyNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentApplicationNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentDomainNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentFirstPartyToolsNamespace,
            "McpTool",
            "CustomToolManager",
            ThirdPartyToolMigrationRuleCatalog.LegacyEditorDelayTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyTimerDelayTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyMainThreadSwitcherTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyPlayerLoopTimingTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyEditorWindowCaptureUtilityTypeName,
            "UnityCliLoopToolRegistrar",
            "ToolInfo"
        };

        internal static async Task<MigrationTargetPreflightResult> FindMigrationTargetAsync(
            string projectRoot,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string assetsDirectory = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsDirectory))
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            DirectorySearchState searchState = new DirectorySearchState(assetsDirectory);
            int inspectedEntryCount = 0;
            while (searchState.HasPendingDirectories)
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationTargetPreflightResult.NoTargets;
                }

                string directoryPath = searchState.PopDirectory();
                MigrationTargetPreflightResult fileResult =
                    await InspectFilesInDirectoryAsync(directoryPath, ct);
                if (fileResult != MigrationTargetPreflightResult.NoTargets)
                {
                    return fileResult;
                }

                foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
                {
                    if (ProjectFileInventory.ShouldExcludeDirectory(projectRoot, childDirectoryPath))
                    {
                        continue;
                    }

                    searchState.PushDirectory(childDirectoryPath);
                    inspectedEntryCount++;
                    if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                    {
                        await Task.Yield();
                    }
                }
            }

            return MigrationTargetPreflightResult.NoTargets;
        }

        internal static MigrationTargetPreflightResult InspectSourceText(
            string source,
            string extension)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");

            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                return InspectCSharpSourceText(source);
            }

            if (string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                return InspectAsmdefSourceText(source);
            }

            return MigrationTargetPreflightResult.NoTargets;
        }

        private static async Task<MigrationTargetPreflightResult> InspectFilesInDirectoryAsync(
            string directoryPath,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            int inspectedEntryCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(directoryPath))
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationTargetPreflightResult.NoTargets;
                }

                string extension = Path.GetExtension(filePath);
                if (!ShouldInspectExtension(extension))
                {
                    continue;
                }

                string source = File.ReadAllText(filePath);
                MigrationTargetPreflightResult result = InspectSourceText(source, extension);
                if (result != MigrationTargetPreflightResult.NoTargets)
                {
                    return result;
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            return MigrationTargetPreflightResult.NoTargets;
        }

        private static bool ShouldInspectExtension(string extension)
        {
            return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase);
        }

        private static MigrationTargetPreflightResult InspectCSharpSourceText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsCSharpCandidateMarker(source))
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            if (ThirdPartyToolMigrationFastSourceTargetDetector.ContainsFastCSharpMigrationTarget(source))
            {
                return MigrationTargetPreflightResult.HasTargets;
            }

            return MigrationTargetPreflightResult.NeedsFullScan;
        }

        private static MigrationTargetPreflightResult InspectAsmdefSourceText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsLegacyAsmdefName(source))
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            if (ThirdPartyToolMigrationFastSourceTargetDetector.ContainsFastAsmdefMigrationTarget(source))
            {
                return MigrationTargetPreflightResult.HasTargets;
            }

            return MigrationTargetPreflightResult.NeedsFullScan;
        }

        private static bool ContainsLegacyAsmdefName(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return source.IndexOf(
                    ThirdPartyToolMigrationRuleCatalog.LegacyEditorAssemblyName,
                    StringComparison.Ordinal) >= 0 ||
                source.IndexOf(
                    ThirdPartyToolMigrationRuleCatalog.LegacyRuntimeAssemblyName,
                    StringComparison.Ordinal) >= 0;
        }

        private static bool ContainsCSharpCandidateMarker(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsAny(source, DirectCSharpCandidateMarkers) ||
                ContainsAnyTypeReplacementRuleMarker(
                    source,
                    ThirdPartyToolMigrationRuleCatalog.ToolContractTypeReplacementRules) ||
                ContainsAnyTypeReplacementRuleMarker(
                    source,
                    ThirdPartyToolMigrationRuleCatalog.DomainTypeReplacementRules) ||
                ContainsAnyTypeReplacementRuleMarker(
                    source,
                    ThirdPartyToolMigrationRuleCatalog.ApplicationTypeReplacementRules) ||
                ContainsAnyTypeReplacementRuleMarker(
                    source,
                    ThirdPartyToolMigrationRuleCatalog.FirstPartyScreenshotTypeReplacementRules);
        }

        private static bool ContainsAnyTypeReplacementRuleMarker(
            string source,
            ThirdPartyToolMigrationParsingRules.TypeReplacementRule[] rules)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(rules != null, "rules must not be null");

            foreach (ThirdPartyToolMigrationParsingRules.TypeReplacementRule rule in rules)
            {
                if (source.IndexOf(rule.LegacyName, StringComparison.Ordinal) >= 0 ||
                    source.IndexOf(rule.CurrentName, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(string source, string[] markers)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(markers != null, "markers must not be null");

            foreach (string marker in markers)
            {
                if (source.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class DirectorySearchState
        {
            private readonly Stack<string> _pendingDirectories = new Stack<string>();

            public DirectorySearchState(string rootDirectory)
            {
                Debug.Assert(!string.IsNullOrEmpty(rootDirectory), "rootDirectory must not be null or empty");

                _pendingDirectories.Push(rootDirectory);
            }

            public bool HasPendingDirectories => _pendingDirectories.Count > 0;

            public string PopDirectory()
            {
                return _pendingDirectories.Pop();
            }

            public void PushDirectory(string directoryPath)
            {
                Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

                _pendingDirectories.Push(directoryPath);
            }
        }
    }

    internal enum MigrationTargetPreflightResult
    {
        NoTargets,
        HasTargets,
        NeedsFullScan
    }
}
