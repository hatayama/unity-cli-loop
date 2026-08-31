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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingTypeResolutionRules
    {
        public static bool ContainsIdentifierTypeNameReference(
            string source,
            string identifier,
            int beforeIndex,
            string expectedTypeName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(identifier), "identifier must not be null or empty");
            Debug.Assert(beforeIndex >= 0, "beforeIndex must not be negative");
            Debug.Assert(expectedTypeName != null, "expectedTypeName must not be null");

            string[] typeNameCandidates = ReadIdentifierTypeNameCandidates(source, identifier, beforeIndex);
            foreach (string typeNameCandidate in typeNameCandidates)
            {
                if (IsTypeNameReference(typeNameCandidate, expectedTypeName))
                {
                    return true;
                }
            }

            return false;
        }

        public static string[] ReadIdentifierTypeNameCandidates(
            string source,
            string identifier,
            int beforeIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(identifier), "identifier must not be null or empty");
            Debug.Assert(beforeIndex >= 0, "beforeIndex must not be negative");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            (int typeBodyStartIndex, int typeBodyEndIndex) =
                ReadInnermostContainingTypeBodyRange(source, codeTextMask, beforeIndex);
            int searchOffset = typeBodyStartIndex < 0
                ? 0
                : ReadActiveTypeMemberStartIndex(source, codeTextMask, typeBodyStartIndex, beforeIndex);
            string searchSource = source.Substring(searchOffset, beforeIndex - searchOffset);
            Regex declarationRegex = new(
                $@"(?<![\w.])(?<type>(?:global::)?[A-Za-z_][A-Za-z0-9_.]*(?:<[^;(){{}}]*>)?)(?:\s*\?)?\s+{Regex.Escape(identifier)}\b(?=\s*(?:=>|[=,);{{]|$))",
                RegexOptions.Compiled);
            MatchCollection matches = declarationRegex.Matches(searchSource);
            string declaredTypeName =
                ReadLastIdentifierTypeNameFromMatches(
                    searchSource,
                    searchOffset,
                    codeTextMask,
                    matches,
                    false);

            if (declaredTypeName.Length == 0)
            {
                if (typeBodyStartIndex < 0)
                {
                    return Array.Empty<string>();
                }

                searchOffset = typeBodyStartIndex;
                searchSource = source.Substring(
                    typeBodyStartIndex,
                    typeBodyEndIndex - typeBodyStartIndex);
                matches = declarationRegex.Matches(searchSource);
                declaredTypeName =
                    ReadLastIdentifierTypeNameFromMatches(
                        searchSource,
                        searchOffset,
                        codeTextMask,
                        matches,
                        true);
                if (declaredTypeName.Length == 0)
                {
                    return Array.Empty<string>();
                }
            }

            if (IsQualifiedMemberTargetExpression(declaredTypeName))
            {
                return new[] { declaredTypeName };
            }

            List<string> typeNameCandidates = new();
            AddTypeNameCandidate(
                typeNameCandidates,
                QualifyTypeName(
                    declaredTypeName,
                    ReadNamespaceName(source, codeTextMask, beforeIndex)));
            AddTypeNameCandidate(typeNameCandidates, declaredTypeName);
            string[] importedNamespaces = ReadImportedNamespaceNames(source, codeTextMask, beforeIndex);
            foreach (string importedNamespace in importedNamespaces)
            {
                AddTypeNameCandidate(typeNameCandidates, $"{importedNamespace}.{declaredTypeName}");
            }

            return typeNameCandidates.ToArray();
        }

        public static string ReadLastIdentifierTypeNameFromMatches(
            string searchSource,
            int searchOffset,
            CodeTextMask codeTextMask,
            MatchCollection matches,
            bool requireTopLevelTypeMember)
        {
            Debug.Assert(searchSource != null, "searchSource must not be null");
            Debug.Assert(searchOffset >= 0, "searchOffset must not be negative");
            Debug.Assert(matches != null, "matches must not be null");

            string declaredTypeName = string.Empty;
            foreach (Match match in matches)
            {
                int sourceMatchIndex = searchOffset + match.Index;
                if (!codeTextMask.IsCodeAt(sourceMatchIndex))
                {
                    continue;
                }

                if (requireTopLevelTypeMember &&
                    !IsTopLevelTypeMemberMatch(searchSource, searchOffset, codeTextMask, match.Index))
                {
                    continue;
                }

                string matchTypeName = match.Groups["type"].Value;
                if (string.Equals(matchTypeName, "var", StringComparison.Ordinal))
                {
                    matchTypeName = ReadVarInitializerTypeName(searchSource, match.Index + match.Length);
                }

                if (matchTypeName.Length > 0)
                {
                    declaredTypeName = matchTypeName;
                }
            }

            return declaredTypeName;
        }

        public static int ReadActiveTypeMemberStartIndex(
            string source,
            CodeTextMask codeTextMask,
            int typeBodyStartIndex,
            int memberIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(typeBodyStartIndex >= 0, "typeBodyStartIndex must not be negative");
            Debug.Assert(memberIndex >= typeBodyStartIndex, "memberIndex must not precede the type body");

            int activeMemberStartIndex = typeBodyStartIndex;
            int blockDepth = 0;
            for (int index = typeBodyStartIndex; index < memberIndex; index++)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                if (source[index] == '{')
                {
                    blockDepth++;
                    continue;
                }

                if (source[index] == '}')
                {
                    blockDepth--;
                    if (blockDepth == 0)
                    {
                        activeMemberStartIndex = index + 1;
                    }

                    continue;
                }

                if (blockDepth == 0 && source[index] == ';')
                {
                    activeMemberStartIndex = index + 1;
                }
            }

            return activeMemberStartIndex;
        }

        public static bool IsTopLevelTypeMemberMatch(
            string searchSource,
            int searchOffset,
            CodeTextMask codeTextMask,
            int matchIndex)
        {
            Debug.Assert(searchSource != null, "searchSource must not be null");
            Debug.Assert(searchOffset >= 0, "searchOffset must not be negative");
            Debug.Assert(matchIndex >= 0, "matchIndex must not be negative");

            int blockDepth = 0;
            int parenthesisDepth = 0;
            for (int index = 0; index < matchIndex; index++)
            {
                int sourceIndex = searchOffset + index;
                if (!codeTextMask.IsCodeAt(sourceIndex))
                {
                    continue;
                }

                if (searchSource[index] == '{')
                {
                    blockDepth++;
                    continue;
                }

                if (searchSource[index] == '}')
                {
                    blockDepth--;
                    continue;
                }

                if (blockDepth != 0)
                {
                    continue;
                }

                if (searchSource[index] == '(')
                {
                    parenthesisDepth++;
                    continue;
                }

                if (searchSource[index] == ')')
                {
                    parenthesisDepth--;
                }
            }

            return blockDepth == 0 && parenthesisDepth == 0;
        }

        public static (int StartIndex, int EndIndex) ReadInnermostContainingTypeBodyRange(
            string source,
            CodeTextMask codeTextMask,
            int memberIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(memberIndex >= 0, "memberIndex must not be negative");

            int startIndex = -1;
            int endIndex = -1;
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

                startIndex = openBraceIndex + 1;
                endIndex = closingBraceIndex;
            }

            return (startIndex, endIndex);
        }

        public static void AddTypeNameCandidate(List<string> typeNameCandidates, string typeName)
        {
            Debug.Assert(typeNameCandidates != null, "typeNameCandidates must not be null");
            Debug.Assert(typeName != null, "typeName must not be null");

            if (typeName.Length == 0 || typeNameCandidates.Contains(typeName))
            {
                return;
            }

            typeNameCandidates.Add(typeName);
        }

    }
}
