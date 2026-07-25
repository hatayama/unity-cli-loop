using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;

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
