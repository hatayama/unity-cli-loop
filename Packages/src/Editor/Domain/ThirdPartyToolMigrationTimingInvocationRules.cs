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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingInvocationRules
    {
        public static int FindInvocationOpenParenthesisIndex(
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

        public static int FindGenericArgumentListEndIndex(
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

        public static bool ShouldMigrateLegacyPlayerLoopTimingCaller(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            string[] arguments,
            RemovedLegacyPlayerLoopTimingSignature removedSignature,
            ThirdPartyToolMigrationTypeHierarchyIndex typeHierarchyIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameIndex >= 0, "methodNameIndex must not be negative");
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(typeHierarchyIndex != null, "typeHierarchyIndex must not be null");

            string[] trimmedArguments = GetTrimmedInvocationArguments(arguments);
            if (trimmedArguments.Length > removedSignature.OriginalParameters.Length)
            {
                return false;
            }

            if (!DoesPlayerLoopTimingCallerTargetRemovedSignature(
                    source,
                    codeTextMask,
                    methodNameIndex,
                    removedSignature,
                    typeHierarchyIndex))
            {
                return false;
            }

            return AreRemainingPlayerLoopTimingCallerArgumentsCompatible(trimmedArguments, removedSignature);
        }

        public static bool DoesPlayerLoopTimingCallerTargetRemovedSignature(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            RemovedLegacyPlayerLoopTimingSignature removedSignature,
            ThirdPartyToolMigrationTypeHierarchyIndex typeHierarchyIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameIndex >= 0, "methodNameIndex must not be negative");
            Debug.Assert(typeHierarchyIndex != null, "typeHierarchyIndex must not be null");

            if (removedSignature.DeclaringTypeName.Length == 0)
            {
                return false;
            }

            string targetExpression = ReadMemberTargetExpressionBeforeMethodName(source, methodNameIndex);
            bool isThisCall = string.Equals(targetExpression, "this", StringComparison.Ordinal);
            if (targetExpression.Length == 0 || isThisCall)
            {
                return DoesImplicitInstanceCallTargetRemovedSignature(
                    source,
                    codeTextMask,
                    methodNameIndex,
                    removedSignature,
                    typeHierarchyIndex,
                    isThisCall);
            }

            if (string.Equals(targetExpression, "base", StringComparison.Ordinal))
            {
                return DoesBaseCallTargetRemovedSignature(
                    source,
                    codeTextMask,
                    methodNameIndex,
                    removedSignature,
                    typeHierarchyIndex);
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

        // An unqualified or this. call binds to a member of the containing class or, when that class does not
        // declare the name, to one inherited from its base classes, which only the project-wide index can follow.
        private static bool DoesImplicitInstanceCallTargetRemovedSignature(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            RemovedLegacyPlayerLoopTimingSignature removedSignature,
            ThirdPartyToolMigrationTypeHierarchyIndex typeHierarchyIndex,
            bool isThisCall)
        {
            // An empty target also comes back for a receiver that is an expression (GetOther().Run(..)); that call
            // binds to whatever the expression returns, not to the containing class, even inside the declaring type.
            if (!isThisCall && IsPrecededByMemberAccess(source, methodNameIndex))
            {
                return false;
            }

            string containingTypeName = ReadContainingTypeName(source, codeTextMask, methodNameIndex);
            if (string.Equals(containingTypeName, removedSignature.DeclaringTypeName, StringComparison.Ordinal))
            {
                return true;
            }

            // new Runner(..) calls a constructor, which is never inherited.
            if (IsPrecededByNewKeyword(source, codeTextMask, methodNameIndex))
            {
                return false;
            }

            return typeHierarchyIndex.IsInheritedMemberReachable(
                containingTypeName,
                removedSignature.MethodName,
                removedSignature.DeclaringTypeName);
        }

        // The ?. and !. accessors also end with '.' right before the method name.
        private static bool IsPrecededByMemberAccess(string source, int methodNameIndex)
        {
            int index = SkipWhitespaceBackward(source, methodNameIndex - 1);
            return index >= 0 && source[index] == '.';
        }

        private static bool IsPrecededByNewKeyword(string source, CodeTextMask codeTextMask, int methodNameIndex)
        {
            int index = methodNameIndex - 1;
            while (index >= 0 && (!codeTextMask.IsCodeAt(index) || char.IsWhiteSpace(source[index])))
            {
                index--;
            }

            const string newKeyword = "new";
            int keywordStartIndex = index - newKeyword.Length + 1;
            if (keywordStartIndex < 0 ||
                string.CompareOrdinal(source, keywordStartIndex, newKeyword, 0, newKeyword.Length) != 0)
            {
                return false;
            }

            return keywordStartIndex == 0 || !IsIdentifierCharacter(source[keywordStartIndex - 1]);
        }

        // A base call resolves against the base class of the containing class, not the containing class itself.
        // The direct base class named in this source is checked first; the project-wide index then follows
        // intermediate classes in other sources. When neither can settle it, the call is left unchanged.
        private static bool DoesBaseCallTargetRemovedSignature(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            RemovedLegacyPlayerLoopTimingSignature removedSignature,
            ThirdPartyToolMigrationTypeHierarchyIndex typeHierarchyIndex)
        {
            if (IsDirectBaseClassDeclaringType(source, codeTextMask, methodNameIndex, removedSignature))
            {
                return true;
            }

            return typeHierarchyIndex.IsBaseMemberReachable(
                ReadContainingTypeName(source, codeTextMask, methodNameIndex),
                removedSignature.MethodName,
                removedSignature.DeclaringTypeName);
        }

        private static bool IsDirectBaseClassDeclaringType(
            string source,
            CodeTextMask codeTextMask,
            int methodNameIndex,
            RemovedLegacyPlayerLoopTimingSignature removedSignature)
        {
            string baseTypeName = ReadContainingClassBaseTypeName(source, codeTextMask, methodNameIndex);
            if (baseTypeName.Length == 0)
            {
                return false;
            }

            // The name is passed as written: QualifyRelativeTypeName must still see a global:: prefix to leave the
            // name unqualified, and IsExactTypeNameReference normalizes it for comparison.
            if (IsExactTypeNameReference(baseTypeName, removedSignature.DeclaringTypeName))
            {
                return true;
            }

            return IsExactTypeNameReference(
                QualifyRelativeTypeName(
                    baseTypeName,
                    ReadNamespaceName(source, codeTextMask, methodNameIndex)),
                removedSignature.DeclaringTypeName);
        }

        public static bool AreRemainingPlayerLoopTimingCallerArgumentsCompatible(
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

        public static bool IsRemovedPlayerLoopTimingParameter(
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

        public static string ReadCallerArgumentForParameter(
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

        public static string ReadNamedArgumentName(string argument)
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

        public static bool IsCancellationTokenParameter(string typeName)
        {
            Debug.Assert(typeName != null, "typeName must not be null");

            return typeName.EndsWith("CancellationToken", StringComparison.Ordinal) ||
                typeName.IndexOf(".CancellationToken", StringComparison.Ordinal) >= 0;
        }

        public static string ReadMemberTargetExpressionBeforeMethodName(string source, int methodNameIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameIndex >= 0, "methodNameIndex must not be negative");

            int index = SkipWhitespaceBackward(source, methodNameIndex - 1);

            if (index < 0 || source[index] != '.')
            {
                return string.Empty;
            }

            index = SkipNullableMemberAccessorSuffix(source, index - 1);

            int expressionEndIndex = index + 1;
            index = ReadMemberTargetStartIndex(source, index);

            return source.Substring(index + 1, expressionEndIndex - index - 1).Trim();
        }

        private static int SkipNullableMemberAccessorSuffix(string source, int index)
        {
            index = SkipWhitespaceBackward(source, index);
            if (index < 0 || (source[index] != '?' && source[index] != '!'))
            {
                return index;
            }

            return SkipWhitespaceBackward(source, index - 1);
        }

        private static int ReadMemberTargetStartIndex(string source, int index)
        {
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

            return index;
        }
    }
}
