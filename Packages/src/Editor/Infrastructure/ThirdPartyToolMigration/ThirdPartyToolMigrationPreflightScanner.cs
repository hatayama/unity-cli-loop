using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Streams project files for cheap migration target checks before the full scan runs.
    /// </summary>
    internal static class ThirdPartyToolMigrationPreflightScanner
    {
        private const string SourceTextPath = "<preflight-source>";

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

        private static readonly string[] AsmdefCandidateMarkers =
        {
            ThirdPartyToolMigrationRuleCatalog.LegacyEditorAssemblyName,
            ThirdPartyToolMigrationRuleCatalog.LegacyRuntimeAssemblyName
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

            Stack<string> pendingDirectories = new Stack<string>();
            pendingDirectories.Push(assetsDirectory);
            int inspectedEntryCount = 0;
            bool needsFullScan = false;
            while (pendingDirectories.Count > 0)
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationTargetPreflightResult.NoTargets;
                }

                string directoryPath = pendingDirectories.Pop();
                MigrationTargetPreflightResult fileResult =
                    await InspectFilesInDirectoryAsync(directoryPath, ct);
                if (fileResult == MigrationTargetPreflightResult.HasTargets)
                {
                    return fileResult;
                }

                if (fileResult == MigrationTargetPreflightResult.NeedsFullScan)
                {
                    needsFullScan = true;
                }

                foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
                {
                    if (ProjectFileInventory.ShouldExcludeDirectory(projectRoot, childDirectoryPath))
                    {
                        continue;
                    }

                    pendingDirectories.Push(childDirectoryPath);
                    inspectedEntryCount++;
                    if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                    {
                        await Task.Yield();
                    }
                }
            }

            return needsFullScan
                ? MigrationTargetPreflightResult.NeedsFullScan
                : MigrationTargetPreflightResult.NoTargets;
        }

        internal static MigrationTargetPreflightResult InspectSourceText(
            string source,
            string extension)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");

            return InspectFileSourceText(source, extension, SourceTextPath);
        }

        private static MigrationTargetPreflightResult InspectFileSourceText(
            string source,
            string extension,
            string filePath)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                return InspectCSharpSourceText(source);
            }

            if (string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                return InspectAsmdefFileSourceText(source, filePath);
            }

            return MigrationTargetPreflightResult.NoTargets;
        }

        private static async Task<MigrationTargetPreflightResult> InspectFilesInDirectoryAsync(
            string directoryPath,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            int inspectedEntryCount = 0;
            bool needsFullScan = false;
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

                (bool wasRead, string source) = TryReadSourceText(filePath);
                if (!wasRead)
                {
                    needsFullScan = true;
                    continue;
                }

                MigrationTargetPreflightResult result = InspectFileSourceText(source, extension, filePath);
                if (result == MigrationTargetPreflightResult.HasTargets)
                {
                    return result;
                }

                if (result == MigrationTargetPreflightResult.NeedsFullScan)
                {
                    needsFullScan = true;
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            return needsFullScan
                ? MigrationTargetPreflightResult.NeedsFullScan
                : MigrationTargetPreflightResult.NoTargets;
        }

        private static (bool WasRead, string Source) TryReadSourceText(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            try
            {
                return (true, ThirdPartyToolMigrationFileAccess.ReadAllText(filePath));
            }
            catch (Exception ex) when (IsSkippablePreflightReadException(ex))
            {
                UnityEngine.Debug.LogWarning(
                    $"[UnityCliLoop] Deferring migration preflight after unreadable file at {filePath}: {ex.Message}");
                return (false, string.Empty);
            }
        }

        private static bool IsSkippablePreflightReadException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            return ex is IOException ||
                   ex is UnauthorizedAccessException;
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

        private static MigrationTargetPreflightResult InspectAsmdefFileSourceText(
            string source,
            string filePath)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            if (!ContainsLegacyAsmdefName(source))
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            if (!ThirdPartyToolMigrationAssemblyReferenceResolver.TryReadJsonObjectForMigration(
                    filePath,
                    _ => source,
                    out JObject asmdef))
            {
                return MigrationTargetPreflightResult.NeedsFullScan;
            }

            if (ContainsLegacyAsmdefReference(asmdef))
            {
                return MigrationTargetPreflightResult.HasTargets;
            }

            return MigrationTargetPreflightResult.NeedsFullScan;
        }

        private static bool ContainsLegacyAsmdefReference(JObject asmdef)
        {
            Debug.Assert(asmdef != null, "asmdef must not be null");

            if (asmdef["references"] is not JArray references)
            {
                return false;
            }

            foreach (JToken referenceToken in references)
            {
                string reference = referenceToken.Value<string>() ?? string.Empty;
                if (string.Equals(
                        reference,
                        ThirdPartyToolMigrationRuleCatalog.LegacyEditorAssemblyName,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        reference,
                        ThirdPartyToolMigrationRuleCatalog.LegacyRuntimeAssemblyName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyAsmdefName(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsAny(source, AsmdefCandidateMarkers);
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
    }

    internal enum MigrationTargetPreflightResult
    {
        NoTargets,
        HasTargets,
        NeedsFullScan
    }
}
