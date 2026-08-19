using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Code Literal Hoister behavior for Unity CLI Loop.
    /// </summary>
    internal static class DynamicCodeLiteralHoister
    {
        internal const string LiteralParameterPrefix = "__uloop_literal_";

        public static HoistedLiteralRewriteResult Rewrite(string source)
        {
            StringBuilder rewrittenSource = new(source.Length);
            List<HoistedLiteralBinding> bindings = new();
            LiteralHoistScopeTracker scopeTracker = new();
            int index = 0;

            while (index < source.Length)
            {
                if (TryCopyProtectedSyntax(source, rewrittenSource, scopeTracker, ref index))
                {
                    continue;
                }

                if (TryCopyScopePunctuation(source, rewrittenSource, scopeTracker, ref index))
                {
                    continue;
                }

                if (TryHoistOrCopyLiterals(source, rewrittenSource, bindings, scopeTracker, ref index))
                {
                    continue;
                }

                rewrittenSource.Append(source[index]);
                index++;
            }

            List<string> declarationLines = new();
            foreach (HoistedLiteralBinding binding in bindings)
            {
                declarationLines.Add(
                    $"{binding.TypeName} {binding.ParameterName} = ({binding.TypeName})parameters[\"{binding.ParameterName}\"];");
            }

            return new HoistedLiteralRewriteResult(
                rewrittenSource.ToString(),
                bindings,
                declarationLines);
        }

        // Copies syntax that must stay verbatim (strings with interpolation/verbatim, comments,
        // and static local-function headers). Why a helper: those scanners are one skip-list,
        // and leaving them inline kept Rewrite over CA1502.
        private static bool TryCopyProtectedSyntax(
            string source,
            StringBuilder rewrittenSource,
            LiteralHoistScopeTracker scopeTracker,
            ref int index)
        {
            if (DynamicCodeLiteralSyntaxScanner.TryCopyInterpolatedStringLiteral(source, rewrittenSource, ref index))
            {
                return true;
            }

            if (DynamicCodeLiteralSyntaxScanner.TryCopyVerbatimStringLiteral(source, rewrittenSource, ref index))
            {
                return true;
            }

            if (DynamicCodeLiteralSyntaxScanner.TryCopyCharLiteral(source, rewrittenSource, ref index))
            {
                return true;
            }

            if (DynamicCodeLiteralSyntaxScanner.TryCopyLineComment(source, rewrittenSource, ref index))
            {
                return true;
            }

            if (DynamicCodeLiteralSyntaxScanner.TryCopyBlockComment(source, rewrittenSource, ref index))
            {
                return true;
            }

            return scopeTracker.TryConsumeStaticLocalFunctionHeader(source, index, rewrittenSource, ref index);
        }

        private static bool TryCopyScopePunctuation(
            string source,
            StringBuilder rewrittenSource,
            LiteralHoistScopeTracker scopeTracker,
            ref int index)
        {
            if (source[index] == '{')
            {
                scopeTracker.OnOpenBrace();
                rewrittenSource.Append('{');
                index++;
                return true;
            }

            if (source[index] == '}')
            {
                scopeTracker.OnCloseBrace();
                rewrittenSource.Append('}');
                index++;
                return true;
            }

            if (source[index] == ';')
            {
                scopeTracker.OnSemicolon();
                rewrittenSource.Append(';');
                index++;
                return true;
            }

            return false;
        }

        private static bool TryHoistOrCopyLiterals(
            string source,
            StringBuilder rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            LiteralHoistScopeTracker scopeTracker,
            ref int index)
        {
            if (scopeTracker.ShouldSuppressLiteralHoisting)
            {
                return DynamicCodeLiteralSyntaxScanner.TryCopyRegularStringLiteral(source, rewrittenSource, ref index);
            }

            if (TryHoistRegularStringLiteral(source, rewrittenSource, bindings, ref index))
            {
                return true;
            }

            return TryHoistIntegerLiteral(source, rewrittenSource, bindings, ref index);
        }

        private static bool TryHoistRegularStringLiteral(
            string source,
            StringBuilder rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            ref int index)
        {
            if (source[index] != '"')
            {
                return false;
            }

            if (index > 0 && (source[index - 1] == '@' || source[index - 1] == '$'))
            {
                return false;
            }

            int start = index;
            index++;

            while (index < source.Length)
            {
                char current = source[index];
                if (current == '\\')
                {
                    DynamicCodeRegularStringLiteralUnescaper.AdvanceEscapedLiteralSequence(source, ref index);
                    continue;
                }

                if (current == '"')
                {
                    index++;
                    string literalToken = source.Substring(start, index - start);
                    if (!DynamicCodeRegularStringLiteralUnescaper.TryUnescapeRegularStringLiteral(literalToken, out string value))
                    {
                        rewrittenSource.Append(literalToken);
                        return true;
                    }

                    string parameterName = CreateParameterName(bindings.Count);
                    bindings.Add(new HoistedLiteralBinding(parameterName, "string", value));
                    rewrittenSource.Append(parameterName);
                    return true;
                }

                index++;
            }

            index = start;
            return false;
        }

        private static bool TryHoistIntegerLiteral(
            string source,
            StringBuilder rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            ref int index)
        {
            char current = source[index];
            if (!char.IsDigit(current))
            {
                return false;
            }

            if (index > 0)
            {
                char previous = source[index - 1];
                if (char.IsLetterOrDigit(previous) || previous == '_' || previous == '.')
                {
                    return false;
                }
            }

            int start = index;
            index++;
            while (index < source.Length && char.IsDigit(source[index]))
            {
                index++;
            }

            if (index < source.Length && (source[index] == 'L' || source[index] == 'l'))
            {
                int suffixIndex = index;
                index++;
                if (!HasIntegerLiteralBoundary(source, index))
                {
                    index = start;
                    return false;
                }

                string longToken = source.Substring(start, suffixIndex - start);
                if (!long.TryParse(longToken, NumberStyles.None, CultureInfo.InvariantCulture, out long longValue))
                {
                    index = start;
                    return false;
                }

                string longParameterName = CreateParameterName(bindings.Count);
                bindings.Add(new HoistedLiteralBinding(longParameterName, "long", longValue));
                rewrittenSource.Append(longParameterName);
                return true;
            }

            if (!HasIntegerLiteralBoundary(source, index))
            {
                index = start;
                return false;
            }

            string token = source.Substring(start, index - start);
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int intValue))
            {
                index = start;
                return false;
            }

            string parameterName = CreateParameterName(bindings.Count);
            bindings.Add(new HoistedLiteralBinding(parameterName, "int", intValue));
            rewrittenSource.Append(parameterName);
            return true;
        }

        private static bool HasIntegerLiteralBoundary(string source, int index)
        {
            if (index >= source.Length)
            {
                return true;
            }

            char next = source[index];
            return !char.IsLetter(next) && !char.IsDigit(next) && next != '_' && next != '.';
        }

        private static string CreateParameterName(int index)
        {
            return $"{LiteralParameterPrefix}{index}";
        }
    }

    /// <summary>
    /// Carries the result data produced by Hoisted Literal Rewrite behavior.
    /// </summary>
    internal sealed class HoistedLiteralRewriteResult
    {
        public string RewrittenSource { get; }
        public List<HoistedLiteralBinding> Bindings { get; }
        public List<string> DeclarationLines { get; }

        public HoistedLiteralRewriteResult(
            string rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            List<string> declarationLines)
        {
            RewrittenSource = rewrittenSource;
            Bindings = bindings;
            DeclarationLines = declarationLines;
        }
    }
}
