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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingDeclarationRules
    {
        public static (
            string Content,
            int ReplacementCount,
            RemovedLegacyPlayerLoopTimingSignature[] RemovedSignatures)
            RemoveLegacyPlayerLoopTimingParametersInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyPlayerLoopTiming,
            string[] migratedCalleeMethodNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(migratedCalleeMethodNames != null, "migratedCalleeMethodNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            StringBuilder builder = new(source.Length);
            List<RemovedLegacyPlayerLoopTimingSignature> removedSignatures = new();
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (i < sourceCopyIndex ||
                    source[i] != '(' ||
                    !codeTextMask.IsCodeAt(i) ||
                    !CanContainMethodParameterList(source, i))
                {
                    continue;
                }

                string methodName = ReadMethodNameBeforeParameterList(source, i);
                if (!IsMethodDeclarationParameterListName(methodName))
                {
                    continue;
                }

                if (IsConstructorDeclarationParameterList(source, codeTextMask, i, methodName))
                {
                    continue;
                }

                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    i);
                if (closingParenthesisIndex < 0)
                {
                    continue;
                }

                string parametersSource = source.Substring(
                    i + 1,
                    closingParenthesisIndex - i - 1);
                (int usageStartIndex, int usageEndIndex) = FindMethodImplementationUsageRange(
                    source,
                    codeTextMask,
                    closingParenthesisIndex + 1);
                if (usageStartIndex < 0)
                {
                    continue;
                }

                string methodUsageSource = source.Substring(
                    usageStartIndex,
                    usageEndIndex - usageStartIndex);
                string[] parameters = SplitAttributeArguments(parametersSource);
                if (IsContractBoundMethodDeclaration(
                        source,
                        codeTextMask,
                        methodName,
                        i,
                        parametersSource))
                {
                    continue;
                }

                (
                    string[] migratedParameters,
                    RemovedLegacyPlayerLoopTimingParameter[] removedParameters) =
                    RemoveLegacyPlayerLoopTimingParameters(
                        parameters,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyPlayerLoopTiming,
                        migratedCalleeMethodNames,
                        methodUsageSource);
                if (removedParameters.Length == 0)
                {
                    continue;
                }

                if (methodName.Length > 0)
                {
                    removedSignatures.Add(
                        new RemovedLegacyPlayerLoopTimingSignature(
                            methodName,
                            ReadContainingTypeName(source, codeTextMask, i),
                            ReadLegacyPlayerLoopTimingParameterDeclarations(parameters),
                            removedParameters));
                }

                builder.Append(source, sourceCopyIndex, i + 1 - sourceCopyIndex);
                builder.Append(string.Join(", ", migratedParameters));
                sourceCopyIndex = closingParenthesisIndex;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0, Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount, removedSignatures.ToArray());
        }

    }
}
