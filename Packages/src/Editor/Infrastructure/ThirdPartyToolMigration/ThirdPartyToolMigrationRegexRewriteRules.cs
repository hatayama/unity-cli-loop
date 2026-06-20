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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingDeclarationRules;
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
    internal static class ThirdPartyToolMigrationRegexRewriteRules
    {
        internal static string ReplaceRegexInCode(
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

        internal static string ReplaceLegacyToolAttributesInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyToolAttribute,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            StringBuilder builder = new(source.Length);
            int localReplacementCount = 0;
            int index = 0;
            while (index < source.Length)
            {
                if (source[index] != '[' || !codeTextMask.IsCodeAt(index))
                {
                    builder.Append(source[index]);
                    index++;
                    continue;
                }

                int closingBracketIndex = FindAttributeListClosingBracketIndex(
                    source,
                    codeTextMask,
                    index + 1);
                if (closingBracketIndex < 0)
                {
                    builder.Append(source[index]);
                    index++;
                    continue;
                }

                string attributesSource = source.Substring(index + 1, closingBracketIndex - index - 1);
                if (!TryMigrateLegacyToolAttributeList(
                        attributesSource,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyToolAttribute,
                        out string migratedAttributes))
                {
                    builder.Append(source, index, closingBracketIndex - index + 1);
                    index = closingBracketIndex + 1;
                    continue;
                }

                builder.Append('[');
                builder.Append(migratedAttributes);
                builder.Append(']');
                localReplacementCount++;
                index = closingBracketIndex + 1;
            }

            replacementCount += localReplacementCount;
            return builder.ToString();
        }

        internal static int FindAttributeListClosingBracketIndex(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int nestedBracketDepth = 0;
            for (int i = startIndex; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                if (source[i] == '[')
                {
                    nestedBracketDepth++;
                    continue;
                }

                if (source[i] != ']')
                {
                    continue;
                }

                if (nestedBracketDepth == 0)
                {
                    return i;
                }

                nestedBracketDepth--;
            }

            return -1;
        }
    }
}
