using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;


using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Domain.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApiDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingTypeNameRules
    {
        public static string[] ReadImportedNamespaceNames(
            string source,
            CodeTextMask codeTextMask,
            int beforeIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(beforeIndex >= 0, "beforeIndex must not be negative");

            Regex usingNamespaceRegex = new(
                @"^\s*using\s+(?!static\b)(?<namespace>(?:global::)?[A-Za-z_][A-Za-z0-9_.]*)\s*;",
                RegexOptions.Compiled | RegexOptions.Multiline);
            MatchCollection matches = usingNamespaceRegex.Matches(source);
            List<string> namespaces = new();
            foreach (Match match in matches)
            {
                if (match.Index >= beforeIndex || !codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                AddTypeNameCandidate(namespaces, match.Groups["namespace"].Value);
            }

            return namespaces.ToArray();
        }

        public static bool IsInstanceMemberTargetExpression(string targetExpression)
        {
            Debug.Assert(targetExpression != null, "targetExpression must not be null");

            return targetExpression.StartsWith("this.", StringComparison.Ordinal) ||
                targetExpression.StartsWith("base.", StringComparison.Ordinal);
        }

        public static string ReadLastMemberIdentifier(string targetExpression)
        {
            Debug.Assert(targetExpression != null, "targetExpression must not be null");

            int lastDotIndex = targetExpression.LastIndexOf('.');
            if (lastDotIndex < 0 || lastDotIndex >= targetExpression.Length - 1)
            {
                return string.Empty;
            }

            return targetExpression.Substring(lastDotIndex + 1);
        }

        public static string ReadVarInitializerTypeName(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int index = ReadNextNonWhitespaceIndex(source, startIndex);
            if (index >= source.Length || source[index] != '=')
            {
                return string.Empty;
            }

            index = ReadNextNonWhitespaceIndex(source, index + 1);
            const string newKeyword = "new";
            if (!StartsWith(source, index, newKeyword))
            {
                return string.Empty;
            }

            int typeStartIndex = ReadNextNonWhitespaceIndex(source, index + newKeyword.Length);
            int typeEndIndex = typeStartIndex;
            int genericDepth = 0;
            while (typeEndIndex < source.Length)
            {
                char current = source[typeEndIndex];
                if (current == '<')
                {
                    genericDepth++;
                    typeEndIndex++;
                    continue;
                }

                if (current == '>')
                {
                    genericDepth--;
                    typeEndIndex++;
                    continue;
                }

                if (genericDepth == 0 &&
                    (char.IsWhiteSpace(current) || current == '(' || current == '{' || current == '['))
                {
                    break;
                }

                typeEndIndex++;
            }

            if (typeEndIndex <= typeStartIndex)
            {
                return string.Empty;
            }

            return source.Substring(typeStartIndex, typeEndIndex - typeStartIndex);
        }

        public static bool IsQualifiedMemberTargetExpression(string targetExpression)
        {
            Debug.Assert(targetExpression != null, "targetExpression must not be null");

            return targetExpression.StartsWith("global::", StringComparison.Ordinal) ||
                targetExpression.IndexOf('.') >= 0;
        }

        public static bool IsExactTypeNameReference(string candidateTypeName, string expectedTypeName)
        {
            Debug.Assert(candidateTypeName != null, "candidateTypeName must not be null");
            Debug.Assert(expectedTypeName != null, "expectedTypeName must not be null");

            if (candidateTypeName.Length == 0 || expectedTypeName.Length == 0)
            {
                return false;
            }

            return string.Equals(
                NormalizeTypeNameForComparison(candidateTypeName),
                NormalizeTypeNameForComparison(expectedTypeName),
                StringComparison.Ordinal);
        }

        public static bool IsTypeNameReference(string candidateTypeName, string expectedTypeName)
        {
            Debug.Assert(candidateTypeName != null, "candidateTypeName must not be null");
            Debug.Assert(expectedTypeName != null, "expectedTypeName must not be null");

            if (candidateTypeName.Length == 0 || expectedTypeName.Length == 0)
            {
                return false;
            }

            string normalizedCandidateTypeName = NormalizeTypeNameForComparison(candidateTypeName);
            string normalizedExpectedTypeName = NormalizeTypeNameForComparison(expectedTypeName);
            if (normalizedExpectedTypeName.IndexOf('.') >= 0)
            {
                return string.Equals(
                    normalizedCandidateTypeName,
                    normalizedExpectedTypeName,
                    StringComparison.Ordinal);
            }

            return string.Equals(
                GetUnqualifiedTypeName(normalizedCandidateTypeName),
                normalizedExpectedTypeName,
                StringComparison.Ordinal);
        }

        public static string NormalizeTypeNameForComparison(string typeName)
        {
            Debug.Assert(typeName != null, "typeName must not be null");

            string normalizedTypeName = typeName.StartsWith("global::", StringComparison.Ordinal)
                ? typeName.Substring("global::".Length)
                : typeName;
            int genericStartIndex = normalizedTypeName.IndexOf('<');
            return genericStartIndex >= 0
                ? normalizedTypeName.Substring(0, genericStartIndex)
                : normalizedTypeName;
        }

        public static string GetUnqualifiedTypeName(string typeName)
        {
            Debug.Assert(typeName != null, "typeName must not be null");

            int lastDotIndex = typeName.LastIndexOf('.');
            return lastDotIndex >= 0
                ? typeName.Substring(lastDotIndex + 1)
                : typeName;
        }
    }
}
