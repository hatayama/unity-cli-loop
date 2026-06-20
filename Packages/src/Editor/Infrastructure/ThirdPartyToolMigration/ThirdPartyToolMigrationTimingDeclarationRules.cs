using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApiDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationTimingDeclarationRules
    {
        internal static (
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
