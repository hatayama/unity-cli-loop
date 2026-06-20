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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationTimingInvocationRules
    {
        internal static int FindInvocationOpenParenthesisIndex(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int index = ReadNextNonWhitespaceIndex(source, startIndex);
            if (index < source.Length && source[index] == '<' && codeTextMask.IsCodeAt(index))
            {
                int closeAngleIndex = FindGenericArgumentListEndIndex(source, codeTextMask, index);
                if (closeAngleIndex < 0)
                {
                    return -1;
                }

                index = ReadNextNonWhitespaceIndex(source, closeAngleIndex + 1);
            }

            if (index < source.Length && source[index] == '(' && codeTextMask.IsCodeAt(index))
            {
                return index;
            }

            return -1;
        }

        internal static int FindGenericArgumentListEndIndex(
            string source,
            CodeTextMask codeTextMask,
            int openAngleIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openAngleIndex >= 0, "openAngleIndex must not be negative");

            int nestingDepth = 0;
            for (int index = openAngleIndex; index < source.Length; index++)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                if (source[index] == '<')
                {
                    nestingDepth++;
                    continue;
                }

                if (source[index] != '>')
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

        internal static bool ShouldMigrateLegacyPlayerLoopTimingCaller(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            string[] arguments,
            RemovedLegacyPlayerLoopTimingSignature removedSignature)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameIndex >= 0, "methodNameIndex must not be negative");
            Debug.Assert(arguments != null, "arguments must not be null");

            string[] trimmedArguments = GetTrimmedInvocationArguments(arguments);
            if (trimmedArguments.Length > removedSignature.OriginalParameters.Length)
            {
                return false;
            }

            if (!DoesPlayerLoopTimingCallerTargetRemovedSignature(
                    source,
                    codeTextMask,
                    methodNameIndex,
                    removedSignature))
            {
                return false;
            }

            return AreRemainingPlayerLoopTimingCallerArgumentsCompatible(trimmedArguments, removedSignature);
        }

        internal static bool DoesPlayerLoopTimingCallerTargetRemovedSignature(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            RemovedLegacyPlayerLoopTimingSignature removedSignature)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameIndex >= 0, "methodNameIndex must not be negative");

            if (removedSignature.DeclaringTypeName.Length == 0)
            {
                return false;
            }

            string targetExpression = ReadMemberTargetExpressionBeforeMethodName(source, methodNameIndex);
            if (targetExpression.Length == 0)
            {
                string containingTypeName = ReadContainingTypeName(source, codeTextMask, methodNameIndex);
                return string.Equals(
                    containingTypeName,
                    removedSignature.DeclaringTypeName,
                    StringComparison.Ordinal);
            }

            if (string.Equals(targetExpression, "this", StringComparison.Ordinal) ||
                string.Equals(targetExpression, "base", StringComparison.Ordinal))
            {
                string containingTypeName = ReadContainingTypeName(source, codeTextMask, methodNameIndex);
                return string.Equals(
                    containingTypeName,
                    removedSignature.DeclaringTypeName,
                    StringComparison.Ordinal);
            }

            if (IsQualifiedMemberTargetExpression(targetExpression))
            {
                if (IsExactTypeNameReference(targetExpression, removedSignature.DeclaringTypeName))
                {
                    return true;
                }

                if (!IsInstanceMemberTargetExpression(targetExpression))
                {
                    string namespaceQualifiedTargetExpression = QualifyRelativeTypeName(
                        targetExpression,
                        ReadNamespaceName(source, codeTextMask, methodNameIndex));
                    return IsExactTypeNameReference(
                        namespaceQualifiedTargetExpression,
                        removedSignature.DeclaringTypeName);
                }

                string memberIdentifier = ReadLastMemberIdentifier(targetExpression);
                if (memberIdentifier.Length == 0)
                {
                    return false;
                }

                return ContainsIdentifierTypeNameReference(
                    source,
                    memberIdentifier,
                    methodNameIndex,
                    removedSignature.DeclaringTypeName);
            }

            if (IsTypeNameReference(targetExpression, removedSignature.DeclaringTypeName))
            {
                return true;
            }

            string targetIdentifierTypeName = QualifyTypeName(
                targetExpression,
                ReadNamespaceName(source, codeTextMask, methodNameIndex));
            if (IsTypeNameReference(targetIdentifierTypeName, removedSignature.DeclaringTypeName))
            {
                return true;
            }

            return ContainsIdentifierTypeNameReference(
                source,
                targetExpression,
                methodNameIndex,
                removedSignature.DeclaringTypeName);
        }

        internal static bool AreRemainingPlayerLoopTimingCallerArgumentsCompatible(
            string[] arguments,
            RemovedLegacyPlayerLoopTimingSignature removedSignature)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            foreach (LegacyPlayerLoopTimingParameterDeclaration parameter in removedSignature.OriginalParameters)
            {
                if (IsRemovedPlayerLoopTimingParameter(parameter.Index, removedSignature.RemovedParameters))
                {
                    continue;
                }

                string argument = ReadCallerArgumentForParameter(arguments, parameter);
                if (argument.Length == 0)
                {
                    if (!parameter.HasDefaultValue)
                    {
                        return false;
                    }

                    continue;
                }

                if (IsCancellationTokenParameter(parameter.TypeName) &&
                    !IsLikelyCancellationTokenArgument(argument))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsRemovedPlayerLoopTimingParameter(
            int parameterIndex,
            RemovedLegacyPlayerLoopTimingParameter[] removedParameters)
        {
            Debug.Assert(parameterIndex >= 0, "parameterIndex must not be negative");
            Debug.Assert(removedParameters != null, "removedParameters must not be null");

            foreach (RemovedLegacyPlayerLoopTimingParameter removedParameter in removedParameters)
            {
                if (removedParameter.Index == parameterIndex)
                {
                    return true;
                }
            }

            return false;
        }

        internal static string ReadCallerArgumentForParameter(
            string[] arguments,
            LegacyPlayerLoopTimingParameterDeclaration parameter)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            foreach (string argument in arguments)
            {
                string namedArgumentValue = GetNamedArgumentValueOrNull(argument, parameter.Name);
                if (namedArgumentValue != null)
                {
                    return namedArgumentValue;
                }
            }

            if (parameter.Index >= arguments.Length)
            {
                return string.Empty;
            }

            string positionalArgument = arguments[parameter.Index];
            string positionalArgumentName = ReadNamedArgumentName(positionalArgument);
            if (positionalArgumentName.Length > 0 &&
                !string.Equals(positionalArgumentName, parameter.Name, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return positionalArgument;
        }

        internal static string ReadNamedArgumentName(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            int colonIndex = FindNamedArgumentColonIndex(argument);
            if (colonIndex <= 0)
            {
                return string.Empty;
            }

            string possibleArgumentName = argument.Substring(0, colonIndex).Trim();
            return IsIdentifierLikeExpression(possibleArgumentName) ? possibleArgumentName : string.Empty;
        }

        internal static bool IsCancellationTokenParameter(string typeName)
        {
            Debug.Assert(typeName != null, "typeName must not be null");

            return typeName.EndsWith("CancellationToken", StringComparison.Ordinal) ||
                typeName.IndexOf(".CancellationToken", StringComparison.Ordinal) >= 0;
        }

        internal static string ReadMemberTargetExpressionBeforeMethodName(string source, int methodNameIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameIndex >= 0, "methodNameIndex must not be negative");

            int index = methodNameIndex - 1;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            if (index < 0 || source[index] != '.')
            {
                return string.Empty;
            }

            index--;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            if (index >= 0 && (source[index] == '?' || source[index] == '!'))
            {
                index--;
                while (index >= 0 && char.IsWhiteSpace(source[index]))
                {
                    index--;
                }
            }

            int expressionEndIndex = index + 1;
            while (index >= 0)
            {
                if (IsIdentifierCharacter(source[index]) || source[index] == '.')
                {
                    index--;
                    continue;
                }

                if (source[index] == ':' && index > 0 && source[index - 1] == ':')
                {
                    index -= 2;
                    continue;
                }

                break;
            }

            return source.Substring(index + 1, expressionEndIndex - index - 1).Trim();
        }
    }
}
