using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Applies deterministic source rewrites for V2 custom tools that need the V3 public contract API.
    /// </summary>
    internal static class ThirdPartyToolMigrationRules
    {
        internal const string LegacyNamespace = "io.github.hatayama.uLoopMCP";
        internal const string CurrentNamespace = "io.github.hatayama.UnityCliLoop.ToolContracts";
        internal const string LegacyEditorAssemblyName = "uLoopMCP.Editor";
        internal const string LegacyEditorAssemblyGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentToolContractsGuidReference = "GUID:fc3fd32eddbee40e39c2d76dc184957b";

        private static readonly string[] ExcludedDirectoryNames =
        {
            ".git",
            "Library",
            "Temp",
            "Logs",
            "obj",
            "bin",
            "Build",
            "Builds"
        };

        private static readonly Regex LegacyToolAttributeWithArgumentsRegex =
            new(@"\[\s*McpTool(?:Attribute)?\s*\([^\]]*\)\s*\]", RegexOptions.Compiled);

        private static readonly Regex LegacyToolAttributeRegex =
            new(@"\[\s*McpTool(?:Attribute)?\s*\]", RegexOptions.Compiled);

        private static readonly ReplacementRule[] CSharpReplacementRules =
        {
            new(Regex.Escape(LegacyNamespace), CurrentNamespace),
            new(@"\bMcpToolAttribute\b", "UnityCliLoopToolAttribute"),
            new(@"\bIUnityTool\b", "IUnityCliLoopTool"),
            new(@"\bAbstractUnityTool\b", "UnityCliLoopTool"),
            new(@"\bBaseToolSchema\b", "UnityCliLoopToolSchema"),
            new(@"\bBaseToolResponse\b", "UnityCliLoopToolResponse"),
            new(@"\bCustomToolManager\b", "UnityCliLoopToolRegistrar")
        };

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSource(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            string migratedContent = source;
            int replacementCount = 0;
            migratedContent = ReplaceRegex(
                migratedContent,
                LegacyToolAttributeWithArgumentsRegex,
                "[UnityCliLoopTool]",
                ref replacementCount);
            migratedContent = ReplaceRegex(
                migratedContent,
                LegacyToolAttributeRegex,
                "[UnityCliLoopTool]",
                ref replacementCount);

            foreach (ReplacementRule rule in CSharpReplacementRules)
            {
                migratedContent = ReplaceRegex(
                    migratedContent,
                    rule.PatternRegex,
                    rule.Replacement,
                    ref replacementCount);
            }

            return new ThirdPartyToolMigrationContentResult(
                migratedContent,
                replacementCount);
        }

        internal static ThirdPartyToolMigrationContentResult MigrateAsmdefSource(
            string source,
            bool hasLegacyCSharpSource)
        {
            Debug.Assert(source != null, "source must not be null");

            JObject asmdef = JObject.Parse(source);
            JArray references = asmdef["references"] as JArray;
            if (references == null)
            {
                return new ThirdPartyToolMigrationContentResult(source, 0);
            }

            int replacementCount = 0;
            HashSet<string> addedReferences = new(StringComparer.Ordinal);
            JArray migratedReferences = new();
            foreach (JToken referenceToken in references)
            {
                string reference = referenceToken.Value<string>() ?? string.Empty;
                string migratedReference = GetMigratedAsmdefReference(reference, hasLegacyCSharpSource);
                if (!string.Equals(reference, migratedReference, StringComparison.Ordinal))
                {
                    replacementCount++;
                }

                if (!addedReferences.Add(migratedReference))
                {
                    continue;
                }

                migratedReferences.Add(migratedReference);
            }

            if (replacementCount == 0)
            {
                return new ThirdPartyToolMigrationContentResult(source, 0);
            }

            asmdef["references"] = migratedReferences;
            return new ThirdPartyToolMigrationContentResult(
                asmdef.ToString(Formatting.Indented),
                replacementCount);
        }

        internal static bool ContainsLegacyCSharpApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (source.Contains(LegacyNamespace)) return true;
            if (LegacyToolAttributeWithArgumentsRegex.IsMatch(source)) return true;
            if (LegacyToolAttributeRegex.IsMatch(source)) return true;

            return CSharpReplacementRules.Any(rule => rule.PatternRegex.IsMatch(source));
        }

        internal static bool IsExcludedDirectoryName(string directoryName)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryName), "directoryName must not be null or empty");

            return ExcludedDirectoryNames.Any(
                excludedDirectoryName => string.Equals(
                    excludedDirectoryName,
                    directoryName,
                    StringComparison.OrdinalIgnoreCase));
        }

        internal static string[] GetExcludedDirectoryNames()
        {
            string[] names = new string[ExcludedDirectoryNames.Length];
            Array.Copy(ExcludedDirectoryNames, names, ExcludedDirectoryNames.Length);
            return names;
        }

        private static string GetMigratedAsmdefReference(string reference, bool hasLegacyCSharpSource)
        {
            if (string.Equals(reference, LegacyEditorAssemblyName, StringComparison.Ordinal))
            {
                return CurrentToolContractsGuidReference;
            }

            if (hasLegacyCSharpSource
                && string.Equals(reference, LegacyEditorAssemblyGuidReference, StringComparison.Ordinal))
            {
                return CurrentToolContractsGuidReference;
            }

            return reference;
        }

        private static string ReplaceRegex(
            string source,
            Regex regex,
            string replacement,
            ref int replacementCount)
        {
            int localReplacementCount = 0;
            string migrated = regex.Replace(
                source,
                _ =>
                {
                    localReplacementCount++;
                    return replacement;
                });
            replacementCount += localReplacementCount;
            return migrated;
        }

        private readonly struct ReplacementRule
        {
            public ReplacementRule(string pattern, string replacement)
            {
                Debug.Assert(!string.IsNullOrEmpty(pattern), "pattern must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(replacement), "replacement must not be null or empty");

                PatternRegex = new Regex(pattern, RegexOptions.Compiled);
                Replacement = replacement;
            }

            public Regex PatternRegex { get; }
            public string Replacement { get; }
        }
    }

    internal readonly struct ThirdPartyToolMigrationContentResult
    {
        public ThirdPartyToolMigrationContentResult(string content, int replacementCount)
        {
            Debug.Assert(content != null, "content must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");

            Content = content ?? string.Empty;
            ReplacementCount = replacementCount;
        }

        public string Content { get; }
        public int ReplacementCount { get; }
        public bool Changed => ReplacementCount > 0;
    }
}
