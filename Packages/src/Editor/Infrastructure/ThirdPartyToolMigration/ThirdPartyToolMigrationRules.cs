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
        internal const string CurrentApplicationNamespace = "io.github.hatayama.UnityCliLoop.Application";
        internal const string LegacyEditorAssemblyName = "uLoopMCP.Editor";
        internal const string LegacyEditorAssemblyGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentApplicationGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentToolContractsGuidReference = "GUID:fc3fd32eddbee40e39c2d76dc184957b";
        private const string DescriptionAttributeArgumentName = "Description";
        private const string DisplayDevelopmentOnlyAttributeArgumentName = "DisplayDevelopmentOnly";
        private const string RequiredSecuritySettingAttributeArgumentName = "RequiredSecuritySetting";
        private const string LegacySecuritySettingsTypeName = "SecuritySettings";
        private const string CurrentSecuritySettingTypeName = "UnityCliLoopSecuritySetting";

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

        private static readonly Regex LegacyNamespaceRegex =
            new(Regex.Escape(LegacyNamespace), RegexOptions.Compiled);

        private static readonly Regex LegacyRegistrarRegex =
            new(@"\bCustomToolManager\b", RegexOptions.Compiled);

        private static readonly Regex LegacyToolAttributeListRegex =
            new(@"\[(?<attributes>[^\]]*\bMcpTool(?:Attribute)?\b[^\]]*)\]", RegexOptions.Compiled);

        private static readonly Regex LegacyToolAttributeEntryRegex =
            new(
                @"^\s*(?<qualifier>io\.github\.hatayama\.uLoopMCP\.)?McpTool(?:Attribute)?\s*(?:\((?<arguments>[\s\S]*)\))?\s*$",
                RegexOptions.Compiled);

        private static readonly ReplacementRule[] CSharpReplacementRules =
        {
            new(
                Regex.Escape($"{LegacyNamespace}.CustomToolManager"),
                $"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar"),
            new(Regex.Escape(LegacyNamespace), CurrentNamespace),
            new(@"\bMcpToolAttribute\b", "UnityCliLoopToolAttribute"),
            new(@"\bIUnityTool\b", "IUnityCliLoopTool"),
            new(@"\bAbstractUnityTool\b", "UnityCliLoopTool"),
            new(@"\bBaseToolSchema\b", "UnityCliLoopToolSchema"),
            new(@"\bBaseToolResponse\b", "UnityCliLoopToolResponse"),
            new(@"\bSecuritySettings\b", CurrentSecuritySettingTypeName),
            new(@"\bCustomToolManager\b", $"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar")
        };

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSource(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            string migratedContent = source;
            bool shouldApplyContractRenames = ContainsLegacyToolMigrationMarker(source);
            int replacementCount = 0;
            migratedContent = ReplaceRegexInCode(
                migratedContent,
                LegacyToolAttributeListRegex,
                MigrateLegacyToolAttributeList,
                ref replacementCount);

            if (shouldApplyContractRenames)
            {
                foreach (ReplacementRule rule in CSharpReplacementRules)
                {
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        rule.PatternRegex,
                        _ => rule.Replacement,
                        ref replacementCount);
                }
            }

            return new ThirdPartyToolMigrationContentResult(
                migratedContent,
                replacementCount);
        }

        internal static ThirdPartyToolMigrationContentResult MigrateAsmdefSource(
            string source,
            bool hasLegacyCSharpSource,
            bool requiresApplicationReference)
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
                string[] migratedReferenceItems = GetMigratedAsmdefReferences(
                    reference,
                    hasLegacyCSharpSource,
                    requiresApplicationReference);
                bool referenceChanged = migratedReferenceItems.Length != 1 ||
                    !string.Equals(migratedReferenceItems[0], reference, StringComparison.Ordinal);
                if (referenceChanged)
                {
                    replacementCount++;
                }

                foreach (string migratedReference in migratedReferenceItems)
                {
                    if (!addedReferences.Add(migratedReference))
                    {
                        continue;
                    }

                    migratedReferences.Add(migratedReference);
                }
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

            return ContainsLegacyToolMigrationMarker(source);
        }

        internal static bool ContainsLegacyRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyToolMigrationMarker(source) && RegexMatchesCode(source, LegacyRegistrarRegex);
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

        private static string[] GetMigratedAsmdefReferences(
            string reference,
            bool hasLegacyCSharpSource,
            bool requiresApplicationReference)
        {
            if (string.Equals(reference, LegacyEditorAssemblyName, StringComparison.Ordinal))
            {
                return GetMigratedLegacyEditorReferences(requiresApplicationReference);
            }

            if (hasLegacyCSharpSource
                && string.Equals(reference, LegacyEditorAssemblyGuidReference, StringComparison.Ordinal))
            {
                return GetMigratedLegacyEditorReferences(requiresApplicationReference);
            }

            return new[] { reference };
        }

        private static string[] GetMigratedLegacyEditorReferences(bool requiresApplicationReference)
        {
            if (!requiresApplicationReference)
            {
                return new[] { CurrentToolContractsGuidReference };
            }

            return new[] { CurrentToolContractsGuidReference, CurrentApplicationGuidReference };
        }

        private static string MigrateLegacyToolAttributeList(Match match)
        {
            Debug.Assert(match != null, "match must not be null");

            string attributesSource = match.Groups["attributes"].Value;
            string[] attributes = SplitAttributeArguments(attributesSource);
            List<string> migratedAttributes = new();
            bool changed = false;
            foreach (string attribute in attributes)
            {
                string trimmedAttribute = attribute.Trim();
                if (TryMigrateLegacyToolAttributeEntry(trimmedAttribute, out string migratedAttribute))
                {
                    migratedAttributes.Add(migratedAttribute);
                    changed = true;
                    continue;
                }

                migratedAttributes.Add(trimmedAttribute);
            }

            if (!changed)
            {
                return match.Value;
            }

            return $"[{string.Join(", ", migratedAttributes)}]";
        }

        private static bool TryMigrateLegacyToolAttributeEntry(string attribute, out string migratedAttribute)
        {
            Debug.Assert(attribute != null, "attribute must not be null");

            Match match = LegacyToolAttributeEntryRegex.Match(attribute);
            if (!match.Success)
            {
                migratedAttribute = string.Empty;
                return false;
            }

            string argumentsSource = match.Groups["arguments"].Value;
            string[] migratedArguments = GetMigratedSupportedAttributeArguments(argumentsSource);
            string attributeName = match.Groups["qualifier"].Success
                ? $"{CurrentNamespace}.UnityCliLoopTool"
                : "UnityCliLoopTool";
            migratedAttribute = migratedArguments.Length == 0
                ? attributeName
                : $"{attributeName}({string.Join(", ", migratedArguments)})";
            return true;
        }

        private static string[] GetMigratedSupportedAttributeArguments(string argumentsSource)
        {
            Debug.Assert(argumentsSource != null, "argumentsSource must not be null");

            List<string> migratedArguments = new();
            string[] arguments = SplitAttributeArguments(argumentsSource);
            foreach (string argument in arguments)
            {
                string trimmedArgument = argument.Trim();
                if (trimmedArgument.Length == 0)
                {
                    continue;
                }

                if (IsNamedAttributeArgument(trimmedArgument, DescriptionAttributeArgumentName))
                {
                    continue;
                }

                if (IsNamedAttributeArgument(trimmedArgument, DisplayDevelopmentOnlyAttributeArgumentName))
                {
                    migratedArguments.Add(trimmedArgument);
                    continue;
                }

                if (IsNamedAttributeArgument(trimmedArgument, RequiredSecuritySettingAttributeArgumentName))
                {
                    migratedArguments.Add(
                        trimmedArgument.Replace(
                            LegacySecuritySettingsTypeName,
                            CurrentSecuritySettingTypeName));
                }
            }

            return migratedArguments.ToArray();
        }

        private static string[] SplitAttributeArguments(string argumentsSource)
        {
            Debug.Assert(argumentsSource != null, "argumentsSource must not be null");

            List<string> arguments = new();
            int argumentStartIndex = 0;
            int nestingDepth = 0;
            bool isInRegularString = false;
            bool isInVerbatimString = false;
            bool isInCharLiteral = false;
            for (int i = 0; i < argumentsSource.Length; i++)
            {
                char current = argumentsSource[i];
                if (isInRegularString)
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"')
                    {
                        isInRegularString = false;
                    }

                    continue;
                }

                if (isInVerbatimString)
                {
                    if (current != '"')
                    {
                        continue;
                    }

                    if (i + 1 < argumentsSource.Length && argumentsSource[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    isInVerbatimString = false;
                    continue;
                }

                if (isInCharLiteral)
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == '\'')
                    {
                        isInCharLiteral = false;
                    }

                    continue;
                }

                if (StartsWith(argumentsSource, i, "@\"") ||
                    StartsWith(argumentsSource, i, "$@\"") ||
                    StartsWith(argumentsSource, i, "@$\""))
                {
                    isInVerbatimString = true;
                    i += GetStringPrefixLength(argumentsSource, i);
                    continue;
                }

                if (StartsWith(argumentsSource, i, "$\""))
                {
                    isInRegularString = true;
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    isInRegularString = true;
                    continue;
                }

                if (current == '\'')
                {
                    isInCharLiteral = true;
                    continue;
                }

                if (current == '(' || current == '[' || current == '{')
                {
                    nestingDepth++;
                    continue;
                }

                if (current == ')' || current == ']' || current == '}')
                {
                    nestingDepth = Math.Max(0, nestingDepth - 1);
                    continue;
                }

                if (current != ',' || nestingDepth != 0)
                {
                    continue;
                }

                arguments.Add(argumentsSource.Substring(argumentStartIndex, i - argumentStartIndex));
                argumentStartIndex = i + 1;
            }

            arguments.Add(argumentsSource.Substring(argumentStartIndex));
            return arguments.ToArray();
        }

        private static bool IsNamedAttributeArgument(string argument, string argumentName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(argument), "argument must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(argumentName), "argumentName must not be null or whitespace");

            if (!argument.StartsWith(argumentName, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = argumentName.Length; i < argument.Length; i++)
            {
                char current = argument[i];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                return current == '=';
            }

            return false;
        }

        private static string ReplaceRegexInCode(
            string source,
            Regex regex,
            Func<Match, string> replacementFactory,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");
            Debug.Assert(replacementFactory != null, "replacementFactory must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            int localReplacementCount = 0;
            string migrated = regex.Replace(
                source,
                match =>
                {
                    if (!codeTextMask.IsCodeAt(match.Index))
                    {
                        return match.Value;
                    }

                    string replacement = replacementFactory(match);
                    if (string.Equals(match.Value, replacement, StringComparison.Ordinal))
                    {
                        return match.Value;
                    }

                    localReplacementCount++;
                    return replacement;
                });
            replacementCount += localReplacementCount;
            return migrated;
        }

        private static bool RegexMatchesCode(string source, Regex regex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = regex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyToolMigrationMarker(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (RegexMatchesCode(source, LegacyNamespaceRegex)) return true;

            return RegexMatchesCode(source, LegacyToolAttributeListRegex);
        }

        private static bool StartsWith(string source, int startIndex, string value)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(value != null, "value must not be null");

            if (startIndex + value.Length > source.Length)
            {
                return false;
            }

            return string.CompareOrdinal(source, startIndex, value, 0, value.Length) == 0;
        }

        private static int GetStringPrefixLength(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
            {
                return 2;
            }

            return 1;
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

        private readonly struct CodeTextMask
        {
            private readonly bool[] _codeCharacters;

            private CodeTextMask(bool[] codeCharacters)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");

                _codeCharacters = codeCharacters;
            }

            public static CodeTextMask Create(string source)
            {
                Debug.Assert(source != null, "source must not be null");

                bool[] codeCharacters = new bool[source.Length];
                for (int i = 0; i < codeCharacters.Length; i++)
                {
                    codeCharacters[i] = true;
                }

                int index = 0;
                while (index < source.Length)
                {
                    int ignoredTextEndIndex = GetIgnoredTextEndIndex(source, index);
                    if (ignoredTextEndIndex == index)
                    {
                        index++;
                        continue;
                    }

                    MarkRangeAsIgnored(codeCharacters, index, ignoredTextEndIndex);
                    index = ignoredTextEndIndex;
                }

                return new CodeTextMask(codeCharacters);
            }

            public bool IsCodeAt(int index)
            {
                if (index < 0 || index >= _codeCharacters.Length)
                {
                    return false;
                }

                return _codeCharacters[index];
            }

            private static int GetIgnoredTextEndIndex(string source, int startIndex)
            {
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");

                if (StartsWith(source, startIndex, "//"))
                {
                    return FindLineCommentEndIndex(source, startIndex);
                }

                if (StartsWith(source, startIndex, "/*"))
                {
                    return FindBlockCommentEndIndex(source, startIndex);
                }

                if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
                {
                    return FindVerbatimStringEndIndex(source, startIndex + 2);
                }

                if (StartsWith(source, startIndex, "@\""))
                {
                    return FindVerbatimStringEndIndex(source, startIndex + 1);
                }

                if (StartsWith(source, startIndex, "$\"\"\""))
                {
                    return FindRawStringEndIndex(source, startIndex + 1);
                }

                if (StartsWith(source, startIndex, "$\""))
                {
                    return FindRegularStringEndIndex(source, startIndex + 1);
                }

                if (StartsWith(source, startIndex, "\"\"\""))
                {
                    return FindRawStringEndIndex(source, startIndex);
                }

                if (source[startIndex] == '"')
                {
                    return FindRegularStringEndIndex(source, startIndex);
                }

                if (source[startIndex] == '\'')
                {
                    return FindCharLiteralEndIndex(source, startIndex);
                }

                return startIndex;
            }

            private static int FindLineCommentEndIndex(string source, int startIndex)
            {
                int index = startIndex;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                return index;
            }

            private static int FindBlockCommentEndIndex(string source, int startIndex)
            {
                int index = startIndex + 2;
                while (index + 1 < source.Length)
                {
                    if (source[index] == '*' && source[index + 1] == '/')
                    {
                        return index + 2;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int FindRegularStringEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (source[index] == '"')
                    {
                        return index + 1;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int FindVerbatimStringEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] != '"')
                    {
                        index++;
                        continue;
                    }

                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                return source.Length;
            }

            private static int FindRawStringEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 3;
                while (index + 2 < source.Length)
                {
                    if (StartsWith(source, index, "\"\"\""))
                    {
                        return index + 3;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int FindCharLiteralEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (source[index] == '\'')
                    {
                        return index + 1;
                    }

                    index++;
                }

                return source.Length;
            }

            private static void MarkRangeAsIgnored(bool[] codeCharacters, int startIndex, int endIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");
                Debug.Assert(endIndex >= startIndex, "endIndex must not be less than startIndex");

                for (int i = startIndex; i < endIndex && i < codeCharacters.Length; i++)
                {
                    codeCharacters[i] = false;
                }
            }
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
