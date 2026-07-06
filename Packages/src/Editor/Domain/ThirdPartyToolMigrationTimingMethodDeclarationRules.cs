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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationTimingMethodDeclarationRules
    {
        public static bool IsMethodDeclarationParameterListName(string methodName)
        {
            Debug.Assert(methodName != null, "methodName must not be null");

            if (methodName.Length == 0)
            {
                return false;
            }

            return !string.Equals(methodName, "async", StringComparison.Ordinal) &&
                !string.Equals(methodName, "delegate", StringComparison.Ordinal) &&
                !string.Equals(methodName, "if", StringComparison.Ordinal) &&
                !string.Equals(methodName, "for", StringComparison.Ordinal) &&
                !string.Equals(methodName, "foreach", StringComparison.Ordinal) &&
                !string.Equals(methodName, "while", StringComparison.Ordinal) &&
                !string.Equals(methodName, "switch", StringComparison.Ordinal) &&
                !string.Equals(methodName, "using", StringComparison.Ordinal) &&
                !string.Equals(methodName, "lock", StringComparison.Ordinal) &&
                !string.Equals(methodName, "catch", StringComparison.Ordinal);
        }

        public static bool IsConstructorDeclarationParameterList(
            string source,
            CodeTextMask codeTextMask,
            int parameterListStartIndex,
            string methodName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(parameterListStartIndex >= 0, "parameterListStartIndex must not be negative");
            Debug.Assert(methodName != null, "methodName must not be null");

            string containingTypeName = ReadContainingTypeName(source, codeTextMask, parameterListStartIndex);
            if (containingTypeName.Length == 0)
            {
                return false;
            }

            return string.Equals(
                GetUnqualifiedTypeName(containingTypeName),
                methodName,
                StringComparison.Ordinal);
        }

        public static bool IsContractBoundMethodDeclaration(
            string source,
            CodeTextMask codeTextMask,
            string methodName,
            int parameterListStartIndex,
            string parametersSource)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodName != null, "methodName must not be null");
            Debug.Assert(parameterListStartIndex >= 0, "parameterListStartIndex must not be negative");
            Debug.Assert(parametersSource != null, "parametersSource must not be null");

            int methodNameStartIndex = ReadMethodNameStartIndexBeforeParameterList(
                source,
                parameterListStartIndex);
            if (methodNameStartIndex < 0)
            {
                return false;
            }

            if (IsVirtualOrOverrideMethodDeclaration(source, methodNameStartIndex))
            {
                return true;
            }

            if (IsExplicitInterfaceMethodDeclaration(source, methodNameStartIndex))
            {
                return true;
            }

            if (IsPossibleExternalContractMethodDeclaration(source, codeTextMask, methodNameStartIndex))
            {
                return true;
            }

            return ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                methodName,
                parametersSource);
        }

        public static int ReadMethodNameStartIndexBeforeParameterList(
            string source,
            int parameterListStartIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(parameterListStartIndex >= 0, "parameterListStartIndex must not be negative");

            int methodNameEndIndex = parameterListStartIndex - 1;
            while (methodNameEndIndex >= 0 && char.IsWhiteSpace(source[methodNameEndIndex]))
            {
                methodNameEndIndex--;
            }

            int methodNameStartIndex = methodNameEndIndex;
            while (methodNameStartIndex >= 0 && IsIdentifierCharacter(source[methodNameStartIndex]))
            {
                methodNameStartIndex--;
            }

            int candidateStartIndex = methodNameStartIndex + 1;
            return candidateStartIndex <= methodNameEndIndex ? candidateStartIndex : -1;
        }

        public static bool IsVirtualOrOverrideMethodDeclaration(string source, int methodNameStartIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameStartIndex >= 0, "methodNameStartIndex must not be negative");

            int lineStartIndex = GetLineStartIndex(source, methodNameStartIndex);
            string declarationPrefix = source.Substring(
                lineStartIndex,
                methodNameStartIndex - lineStartIndex);
            return ContainsIdentifierInCode(declarationPrefix, "override") ||
                ContainsIdentifierInCode(declarationPrefix, "virtual");
        }

        public static bool IsExplicitInterfaceMethodDeclaration(string source, int methodNameStartIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameStartIndex >= 0, "methodNameStartIndex must not be negative");

            int previousIndex = methodNameStartIndex - 1;
            while (previousIndex >= 0 && char.IsWhiteSpace(source[previousIndex]))
            {
                previousIndex--;
            }

            return previousIndex >= 0 && source[previousIndex] == '.';
        }

        public static bool IsPossibleExternalContractMethodDeclaration(
            string source,
            CodeTextMask codeTextMask,
            int methodNameStartIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameStartIndex >= 0, "methodNameStartIndex must not be negative");

            if (!IsPublicMethodDeclaration(source, methodNameStartIndex))
            {
                return false;
            }

            return IsInsideTypeWithBaseList(source, codeTextMask, methodNameStartIndex);
        }

        public static bool IsPublicMethodDeclaration(string source, int methodNameStartIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(methodNameStartIndex >= 0, "methodNameStartIndex must not be negative");

            int lineStartIndex = GetLineStartIndex(source, methodNameStartIndex);
            string declarationPrefix = source.Substring(
                lineStartIndex,
                methodNameStartIndex - lineStartIndex);
            return ContainsIdentifierInCode(declarationPrefix, "public");
        }

        public static bool IsInsideTypeWithBaseList(
            string source,
            CodeTextMask codeTextMask,
            int memberIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(memberIndex >= 0, "memberIndex must not be negative");

            MatchCollection matches = TypeDeclarationNameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (match.Index >= memberIndex || !codeTextMask.IsCodeAt(match.Index))
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

                string typeDeclarationHeader = source.Substring(
                    match.Index,
                    openBraceIndex - match.Index);
                if (typeDeclarationHeader.IndexOf(':') >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsInterfaceMethodContract(
            string source,
            CodeTextMask codeTextMask,
            string methodName,
            string parametersSource)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty");
            Debug.Assert(parametersSource != null, "parametersSource must not be null");

            int parameterCount = CountParameters(parametersSource);
            MatchCollection interfaceMatches = InterfaceDeclarationNameRegex.Matches(source);
            foreach (Match interfaceMatch in interfaceMatches)
            {
                if (!codeTextMask.IsCodeAt(interfaceMatch.Index))
                {
                    continue;
                }

                int openBraceIndex = FindTypeBodyOpenBraceIndex(
                    source,
                    codeTextMask,
                    interfaceMatch.Index + interfaceMatch.Length);
                if (openBraceIndex < 0)
                {
                    continue;
                }

                int closingBraceIndex = FindBlockClosingBraceIndex(source, codeTextMask, openBraceIndex);
                if (closingBraceIndex <= openBraceIndex)
                {
                    continue;
                }

                if (ContainsInterfaceMethodContractInBody(
                        source,
                        codeTextMask,
                        methodName,
                        parameterCount,
                        openBraceIndex + 1,
                        closingBraceIndex))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsInterfaceMethodContractInBody(
            string source,
            CodeTextMask codeTextMask,
            string methodName,
            int parameterCount,
            int interfaceBodyStartIndex,
            int interfaceBodyEndIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty");
            Debug.Assert(parameterCount >= 0, "parameterCount must not be negative");
            Debug.Assert(interfaceBodyStartIndex >= 0, "interfaceBodyStartIndex must not be negative");
            Debug.Assert(interfaceBodyEndIndex >= interfaceBodyStartIndex, "interfaceBodyEndIndex must be valid");

            Regex methodRegex = new($@"(?<![\w.]){Regex.Escape(methodName)}\s*\(", RegexOptions.Compiled);
            string interfaceBody = source.Substring(
                interfaceBodyStartIndex,
                interfaceBodyEndIndex - interfaceBodyStartIndex);
            MatchCollection methodMatches = methodRegex.Matches(interfaceBody);
            foreach (Match methodMatch in methodMatches)
            {
                int parameterListStartIndex =
                    interfaceBodyStartIndex + methodMatch.Index + methodMatch.Length - 1;
                if (!codeTextMask.IsCodeAt(parameterListStartIndex))
                {
                    continue;
                }

                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    parameterListStartIndex);
                if (closingParenthesisIndex < 0 || closingParenthesisIndex > interfaceBodyEndIndex)
                {
                    continue;
                }

                string interfaceParametersSource = source.Substring(
                    parameterListStartIndex + 1,
                    closingParenthesisIndex - parameterListStartIndex - 1);
                if (CountParameters(interfaceParametersSource) == parameterCount)
                {
                    return true;
                }
            }

            return false;
        }

        public static int CountParameters(string parametersSource)
        {
            Debug.Assert(parametersSource != null, "parametersSource must not be null");

            return SplitAttributeArguments(parametersSource)
                .Select(parameter => parameter.Trim())
                .Count(parameter => parameter.Length > 0);
        }

        public static bool CanContainMethodParameterList(string source, int openParenthesisIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openParenthesisIndex >= 0, "openParenthesisIndex must not be negative");

            char previousCharacter = ReadPreviousNonWhitespaceCharacter(source, openParenthesisIndex);
            return IsIdentifierCharacter(previousCharacter) || previousCharacter == '>';
        }

        public static string ReadMethodNameBeforeParameterList(string source, int openParenthesisIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openParenthesisIndex >= 0, "openParenthesisIndex must not be negative");

            int index = openParenthesisIndex - 1;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            if (index >= 0 && source[index] == '>')
            {
                int genericStartIndex = FindGenericArgumentListStartIndex(source, index);
                if (genericStartIndex < 0)
                {
                    return string.Empty;
                }

                index = genericStartIndex - 1;
                while (index >= 0 && char.IsWhiteSpace(source[index]))
                {
                    index--;
                }
            }

            int identifierEndIndex = index + 1;
            while (index >= 0 && IsIdentifierCharacter(source[index]))
            {
                index--;
            }

            return source.Substring(index + 1, identifierEndIndex - index - 1);
        }
    }
}
