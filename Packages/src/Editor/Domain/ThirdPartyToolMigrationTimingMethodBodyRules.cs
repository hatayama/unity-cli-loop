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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingMethodBodyRules
    {
        public static (int StartIndex, int EndIndex) FindMethodImplementationUsageRange(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int i = startIndex; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                if (source[i] == '{')
                {
                    int blockEndIndex = FindBlockClosingBraceIndex(source, codeTextMask, i);
                    if (blockEndIndex < 0)
                    {
                        return (-1, -1);
                    }

                    return (i + 1, blockEndIndex);
                }

                if (source[i] == '=' &&
                    i + 1 < source.Length &&
                    source[i + 1] == '>' &&
                    codeTextMask.IsCodeAt(i + 1))
                {
                    int expressionEndIndex = FindExpressionBodiedMemberSemicolonIndex(
                        source,
                        codeTextMask,
                        i + 2);
                    if (expressionEndIndex < 0)
                    {
                        return (-1, -1);
                    }

                    return (i + 2, expressionEndIndex);
                }

                if (source[i] == ';' || source[i] == '=')
                {
                    return (-1, -1);
                }
            }

            return (-1, -1);
        }

        public static int FindBlockClosingBraceIndex(
            string source,
            CodeTextMask codeTextMask,
            int openBraceIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openBraceIndex >= 0, "openBraceIndex must not be negative");

            int nestedBraceDepth = 0;
            for (int i = openBraceIndex + 1; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                if (source[i] == '{')
                {
                    nestedBraceDepth++;
                    continue;
                }

                if (source[i] != '}')
                {
                    continue;
                }

                if (nestedBraceDepth == 0)
                {
                    return i;
                }

                nestedBraceDepth--;
            }

            return -1;
        }

        public static int FindExpressionBodiedMemberSemicolonIndex(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int nestedParenthesisDepth = 0;
            int nestedBracketDepth = 0;
            int nestedBraceDepth = 0;
            for (int i = startIndex; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                char current = source[i];
                if (current == '(')
                {
                    nestedParenthesisDepth++;
                    continue;
                }

                if (current == ')')
                {
                    nestedParenthesisDepth--;
                    continue;
                }

                if (current == '[')
                {
                    nestedBracketDepth++;
                    continue;
                }

                if (current == ']')
                {
                    nestedBracketDepth--;
                    continue;
                }

                if (current == '{')
                {
                    nestedBraceDepth++;
                    continue;
                }

                if (current == '}')
                {
                    nestedBraceDepth--;
                    continue;
                }

                if (current == ';' &&
                    nestedParenthesisDepth == 0 &&
                    nestedBracketDepth == 0 &&
                    nestedBraceDepth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public static (
            string[] Parameters,
            RemovedLegacyPlayerLoopTimingParameter[] RemovedParameters)
            RemoveLegacyPlayerLoopTimingParameters(
            string[] parameters,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyPlayerLoopTiming,
            string[] migratedCalleeMethodNames,
            string methodBody)
        {
            Debug.Assert(parameters != null, "parameters must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(migratedCalleeMethodNames != null, "migratedCalleeMethodNames must not be null");
            Debug.Assert(methodBody != null, "methodBody must not be null");

            bool canRemoveTimingParameter =
                CanRemoveLegacyPlayerLoopTimingParameterFromMethod(methodBody, migratedCalleeMethodNames);
            List<string> migratedParameters = new();
            List<RemovedLegacyPlayerLoopTimingParameter> removedParameters = new();
            int parameterIndex = 0;
            foreach (string parameter in parameters)
            {
                string trimmedParameter = parameter.Trim();
                if (trimmedParameter.Length == 0)
                {
                    continue;
                }

                (bool isLegacyPlayerLoopTimingParameter, string parameterName) =
                    ReadLegacyPlayerLoopTimingParameter(
                        trimmedParameter,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyPlayerLoopTiming);
                if (isLegacyPlayerLoopTimingParameter &&
                    canRemoveTimingParameter &&
                    !ContainsIdentifierInCode(methodBody, parameterName))
                {
                    removedParameters.Add(
                        new RemovedLegacyPlayerLoopTimingParameter(
                            parameterIndex,
                            parameterName));
                    parameterIndex++;
                    continue;
                }

                migratedParameters.Add(trimmedParameter);
                parameterIndex++;
            }

            return (migratedParameters.ToArray(), removedParameters.ToArray());
        }
    }
}
