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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingTypeScopeRules
    {
        public static int FindGenericArgumentListStartIndex(string source, int closeAngleIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(closeAngleIndex >= 0, "closeAngleIndex must not be negative");

            int nestingDepth = 0;
            for (int index = closeAngleIndex; index >= 0; index--)
            {
                if (source[index] == '>')
                {
                    nestingDepth++;
                    continue;
                }

                if (source[index] != '<')
                {
                    continue;
                }

                nestingDepth--;
                if (nestingDepth == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        public static string ReadContainingTypeName(
            string source,
            CodeTextMask codeTextMask,
            int memberIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(memberIndex >= 0, "memberIndex must not be negative");

            List<string> containingTypeNames = new();
            MatchCollection matches = TypeDeclarationNameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (match.Index >= memberIndex)
                {
                    break;
                }

                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                int openBraceIndex = FindTypeBodyOpenBraceIndex(source, codeTextMask, match.Index + match.Length);
                if (openBraceIndex < 0 || openBraceIndex >= memberIndex)
                {
                    continue;
                }

                int closingBraceIndex = FindBlockClosingBraceIndex(source, codeTextMask, openBraceIndex);
                if (closingBraceIndex < memberIndex)
                {
                    continue;
                }

                containingTypeNames.Add(match.Groups["name"].Value);
            }

            if (containingTypeNames.Count == 0)
            {
                return string.Empty;
            }

            return QualifyNestedTypeName(
                string.Join(".", containingTypeNames),
                ReadNamespaceName(source, codeTextMask, memberIndex));
        }

        public static string QualifyNestedTypeName(string nestedTypeName, string namespaceName)
        {
            Debug.Assert(!string.IsNullOrEmpty(nestedTypeName), "nestedTypeName must not be null or empty");
            Debug.Assert(namespaceName != null, "namespaceName must not be null");

            return namespaceName.Length == 0
                ? nestedTypeName
                : $"{namespaceName}.{nestedTypeName}";
        }

        public static string QualifyRelativeTypeName(string typeName, string namespaceName)
        {
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");
            Debug.Assert(namespaceName != null, "namespaceName must not be null");

            if (namespaceName.Length == 0 || typeName.StartsWith("global::", StringComparison.Ordinal))
            {
                return typeName;
            }

            return $"{namespaceName}.{typeName}";
        }

        public static string ReadNamespaceName(
            string source,
            CodeTextMask codeTextMask,
            int memberIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(memberIndex >= 0, "memberIndex must not be negative");

            string namespaceName = string.Empty;
            MatchCollection matches = NamespaceDeclarationRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (match.Index >= memberIndex)
                {
                    break;
                }

                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                string candidateNamespaceName = match.Groups["name"].Value;
                string terminator = match.Groups["terminator"].Value;
                if (string.Equals(terminator, ";", StringComparison.Ordinal))
                {
                    namespaceName = candidateNamespaceName;
                    continue;
                }

                int openBraceIndex = match.Groups["terminator"].Index;
                int closingBraceIndex = FindBlockClosingBraceIndex(source, codeTextMask, openBraceIndex);
                if (closingBraceIndex >= memberIndex)
                {
                    namespaceName = candidateNamespaceName;
                }
            }

            return namespaceName;
        }

        public static string QualifyTypeName(string typeName, string namespaceName)
        {
            Debug.Assert(typeName != null, "typeName must not be null");
            Debug.Assert(namespaceName != null, "namespaceName must not be null");

            if (typeName.Length == 0 ||
                namespaceName.Length == 0 ||
                typeName.StartsWith("global::", StringComparison.Ordinal) ||
                typeName.IndexOf('.') >= 0)
            {
                return typeName;
            }

            return $"{namespaceName}.{typeName}";
        }

        public static int FindTypeBodyOpenBraceIndex(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int index = startIndex; index < source.Length; index++)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                if (source[index] == '{')
                {
                    return index;
                }

                if (source[index] == ';')
                {
                    return -1;
                }
            }

            return -1;
        }
    }
}
