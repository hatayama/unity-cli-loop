using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Validates Assembly Definition (.asmdef) configuration before compilation.
    /// </summary>
    public class AssemblyDefinitionDuplicationValidationService
    {
        private static readonly Regex AsmdefNameRegex =
            new("\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex DuplicateAsmdefConsoleRegex = new(
            "^Assembly with name '(?<name>[^']+)' already exists \\((?<path>[^)]+)\\)$",
            RegexOptions.Compiled
        );

        public ValidationResult ValidateNoDuplicateAsmdefNamesFromConsoleErrors()
        {
            LogRetrievalService retrievalService = new();
            LogDisplayDto logData = retrievalService.GetLogsWithSearch(
                UnityCliLoopLogType.Error,
                "Assembly with name '",
                useRegex: false,
                searchInStackTrace: false
            );

            Dictionary<string, List<string>> pathsByAsmName = new(StringComparer.Ordinal);

            foreach (LogEntryDto entry in logData.LogEntries)
            {
                if (string.IsNullOrEmpty(entry.Message))
                {
                    continue;
                }

                Match match = DuplicateAsmdefConsoleRegex.Match(entry.Message.Trim());
                if (!match.Success)
                {
                    continue;
                }

                string asmName = match.Groups["name"].Value;
                string assetPath = match.Groups["path"].Value;
                if (string.IsNullOrEmpty(asmName) || string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                // Prevent false positives from stale console logs by verifying the asset still exists.
                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (obj == null)
                {
                    continue;
                }

                if (!pathsByAsmName.TryGetValue(asmName, out List<string> paths))
                {
                    paths = new List<string>();
                    pathsByAsmName.Add(asmName, paths);
                }

                if (!paths.Contains(assetPath))
                {
                    paths.Add(assetPath);
                }
            }

            if (pathsByAsmName.Count == 0)
            {
                return ValidationResult.Success();
            }

            string details = string.Join(
                "\n",
                pathsByAsmName
                    .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                    .Take(5)
                    .Select(d =>
                    {
                        string paths = string.Join("\n  ", d.Value.Take(8));
                        return $"- {d.Key}\n  {paths}";
                    })
            );

            string message =
                $"{UnityCliLoopConstants.ERROR_MESSAGE_DUPLICATE_ASMDEF}\n" +
                "Detected from Console errors:\n" +
                $"{details}\n" +
                "Fix: ensure each .asmdef has a unique \"name\".";

            return ValidationResult.Failure(message);
        }

        public ValidationResult ValidateNoDuplicateAsmdefNames()
        {
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            Dictionary<string, List<string>> pathsByAsmName = new(StringComparer.Ordinal);

            foreach (string guid in asmdefGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                AssemblyDefinitionAsset asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(assetPath);
                if (asmdef == null)
                {
                    continue;
                }

                string asmName = GetAsmdefName(asmdef);
                if (!pathsByAsmName.TryGetValue(asmName, out List<string> paths))
                {
                    paths = new List<string>();
                    pathsByAsmName.Add(asmName, paths);
                }

                paths.Add(assetPath);
            }

            KeyValuePair<string, List<string>>[] duplicates = pathsByAsmName
                .Where(kvp => kvp.Value.Count > 1)
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .ToArray();

            if (duplicates.Length == 0)
            {
                return ValidationResult.Success();
            }

            string details = string.Join(
                "\n",
                duplicates
                    .Take(5)
                    .Select(d =>
                    {
                        string paths = string.Join("\n  ", d.Value.Take(8));
                        return $"- {d.Key}\n  {paths}";
                    })
            );

            string message =
                $"{UnityCliLoopConstants.ERROR_MESSAGE_DUPLICATE_ASMDEF}\n" +
                "Duplicates (first 5 groups):\n" +
                $"{details}\n" +
                "Fix: ensure each .asmdef has a unique \"name\".";

            return ValidationResult.Failure(message);
        }

        private static string GetAsmdefName(AssemblyDefinitionAsset asmdef)
        {
            string json = asmdef.text;
            if (string.IsNullOrEmpty(json))
            {
                return asmdef.name;
            }

            Match match = AsmdefNameRegex.Match(json);
            if (match.Success)
            {
                string name = match.Groups["name"].Value;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return asmdef.name;
        }
    }
}
