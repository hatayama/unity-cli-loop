#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Holds either a parsed hit condition or the error that prevented arming it.
    /// </summary>
    internal sealed class UloopPausePointHitWhenParseResult
    {
        public UloopPausePointHitWhenParseResult(UloopPausePointHitWhenCondition condition, string errorMessage)
        {
            Condition = condition;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public UloopPausePointHitWhenCondition Condition { get; }
        public string ErrorMessage { get; }
    }

    /// <summary>
    /// Holds the match decision or a recoverable error from evaluating a captured frame.
    /// </summary>
    internal sealed class UloopPausePointHitWhenEvaluation
    {
        public UloopPausePointHitWhenEvaluation(bool matched, string errorMessage)
        {
            Matched = matched;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Matched { get; }
        public string ErrorMessage { get; }
    }

    /// <summary>
    /// Parses and evaluates the intentionally small hit-when condition language.
    /// </summary>
    internal sealed class UloopPausePointHitWhenCondition
    {
        private const string GrammarError = "--hit-when must use '<name> <op> <literal>' where name is an identifier or this and op is ==, !=, >, >=, <, or <=.";
        private const string LiteralError = "--hit-when literal must be null, true, false, an invariant number, or a quoted string.";
        private const string OrderingOperatorError = "--hit-when ordering operators require a numeric literal.";
        private static readonly Regex ExpressionPattern = new Regex(
            "^\\s*(this|[A-Za-z_][A-Za-z0-9_]*)\\s*(==|!=|>=|<=|>|<)\\s*(.+?)\\s*$",
            RegexOptions.CultureInvariant);

        private readonly string _variableName;
        private readonly UloopPausePointHitWhenOperator _operator;
        private readonly UloopPausePointHitWhenLiteralKind _literalKind;
        private readonly bool _booleanLiteral;
        private readonly double _numericLiteral;
        private readonly string _stringLiteral;

        private UloopPausePointHitWhenCondition(
            string expression,
            string variableName,
            UloopPausePointHitWhenOperator comparisonOperator,
            UloopPausePointHitWhenLiteralKind literalKind,
            bool booleanLiteral,
            double numericLiteral,
            string stringLiteral)
        {
            Expression = expression;
            _variableName = variableName;
            _operator = comparisonOperator;
            _literalKind = literalKind;
            _booleanLiteral = booleanLiteral;
            _numericLiteral = numericLiteral;
            _stringLiteral = stringLiteral;
        }

        public string Expression { get; }

        /// <summary>
        /// Parses the condition before it is attached to an enabled pause point.
        /// </summary>
        public static UloopPausePointHitWhenParseResult Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return new UloopPausePointHitWhenParseResult(null, GrammarError);
            }

            Match match = ExpressionPattern.Match(expression);
            if (!match.Success)
            {
                return new UloopPausePointHitWhenParseResult(null, GrammarError);
            }

            string variableName = match.Groups[1].Value;
            string operatorText = match.Groups[2].Value;
            string literalText = match.Groups[3].Value;
            UloopPausePointHitWhenOperator comparisonOperator = ParseOperator(operatorText);
            UloopPausePointHitWhenLiteralKind literalKind = UloopPausePointHitWhenLiteralKind.None;
            bool booleanLiteral = false;
            double numericLiteral = 0;
            string stringLiteral = string.Empty;

            if (string.Equals(literalText, "null", StringComparison.Ordinal))
            {
                literalKind = UloopPausePointHitWhenLiteralKind.Null;
            }
            else if (string.Equals(literalText, "true", StringComparison.Ordinal))
            {
                literalKind = UloopPausePointHitWhenLiteralKind.Boolean;
                booleanLiteral = true;
            }
            else if (string.Equals(literalText, "false", StringComparison.Ordinal))
            {
                literalKind = UloopPausePointHitWhenLiteralKind.Boolean;
            }
            else if (IsQuotedString(literalText))
            {
                literalKind = UloopPausePointHitWhenLiteralKind.String;
                stringLiteral = literalText.Substring(1, literalText.Length - 2);
            }
            else if (double.TryParse(
                         literalText,
                         NumberStyles.Float,
                         CultureInfo.InvariantCulture,
                         out double parsedNumber))
            {
                literalKind = UloopPausePointHitWhenLiteralKind.Number;
                numericLiteral = parsedNumber;
            }
            else
            {
                return new UloopPausePointHitWhenParseResult(null, LiteralError);
            }

            if (literalKind != UloopPausePointHitWhenLiteralKind.Number
                && IsOrderingOperator(comparisonOperator))
            {
                return new UloopPausePointHitWhenParseResult(null, OrderingOperatorError);
            }

            UloopPausePointHitWhenCondition condition = new UloopPausePointHitWhenCondition(
                expression,
                variableName,
                comparisonOperator,
                literalKind,
                booleanLiteral,
                numericLiteral,
                stringLiteral);
            return new UloopPausePointHitWhenParseResult(condition, string.Empty);
        }

        /// <summary>
        /// Evaluates this condition against captured variables in their collector-defined precedence order.
        /// </summary>
        public UloopPausePointHitWhenEvaluation Evaluate(IReadOnlyList<UloopPausePointCapturedVariableEntry> entries)
        {
            UloopPausePointCapturedVariableEntry entry = FindEntry(entries);
            if (entry == null)
            {
                return new UloopPausePointHitWhenEvaluation(
                    false,
                    $"--hit-when could not find variable '{_variableName}' in the captured frame.");
            }

            if (_literalKind == UloopPausePointHitWhenLiteralKind.Null)
            {
                return EvaluateEquality(entry.Value == null);
            }

            if (_literalKind == UloopPausePointHitWhenLiteralKind.Boolean)
            {
                if (!(entry.Value is bool))
                {
                    return new UloopPausePointHitWhenEvaluation(
                        false,
                        $"--hit-when expected variable '{_variableName}' to be Boolean.");
                }

                bool capturedBoolean = (bool)entry.Value;
                return EvaluateEquality(capturedBoolean == _booleanLiteral);
            }

            if (_literalKind == UloopPausePointHitWhenLiteralKind.String)
            {
                if (!(entry.Value is string))
                {
                    return new UloopPausePointHitWhenEvaluation(
                        false,
                        $"--hit-when expected variable '{_variableName}' to be String.");
                }

                string capturedString = (string)entry.Value;
                return EvaluateEquality(string.Equals(capturedString, _stringLiteral, StringComparison.Ordinal));
            }

            if (!IsNumericPrimitive(entry.Value))
            {
                return new UloopPausePointHitWhenEvaluation(
                    false,
                    $"--hit-when expected variable '{_variableName}' to be a numeric primitive.");
            }

            double capturedNumber = Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture);
            return EvaluateNumber(capturedNumber);
        }

        // Keeps operator parsing explicit because the grammar accepts a closed set and should not
        // silently begin accepting enum spellings or future operators.
        private static UloopPausePointHitWhenOperator ParseOperator(string operatorText)
        {
            if (string.Equals(operatorText, "==", StringComparison.Ordinal))
            {
                return UloopPausePointHitWhenOperator.Equal;
            }

            if (string.Equals(operatorText, "!=", StringComparison.Ordinal))
            {
                return UloopPausePointHitWhenOperator.NotEqual;
            }

            if (string.Equals(operatorText, ">", StringComparison.Ordinal))
            {
                return UloopPausePointHitWhenOperator.GreaterThan;
            }

            if (string.Equals(operatorText, ">=", StringComparison.Ordinal))
            {
                return UloopPausePointHitWhenOperator.GreaterThanOrEqual;
            }

            if (string.Equals(operatorText, "<", StringComparison.Ordinal))
            {
                return UloopPausePointHitWhenOperator.LessThan;
            }

            return UloopPausePointHitWhenOperator.LessThanOrEqual;
        }

        // The parse-time literal rule makes this check the single authority for rejecting ordering
        // comparisons that cannot be evaluated consistently across runtime value types.
        private static bool IsOrderingOperator(UloopPausePointHitWhenOperator comparisonOperator)
        {
            return comparisonOperator == UloopPausePointHitWhenOperator.GreaterThan
                || comparisonOperator == UloopPausePointHitWhenOperator.GreaterThanOrEqual
                || comparisonOperator == UloopPausePointHitWhenOperator.LessThan
                || comparisonOperator == UloopPausePointHitWhenOperator.LessThanOrEqual;
        }

        // Quoted strings intentionally have no escape syntax, keeping this arm-time filter small
        // and avoiding a second language with string interpolation or member access semantics.
        private static bool IsQuotedString(string literalText)
        {
            if (literalText.Length < 2)
            {
                return false;
            }

            char firstCharacter = literalText[0];
            char lastCharacter = literalText[literalText.Length - 1];
            return (firstCharacter == '\'' || firstCharacter == '"') && firstCharacter == lastCharacter;
        }

        // Convert.ToDouble is safe only after this exact primitive whitelist, so enums, chars,
        // strings, and arbitrary objects become recoverable evaluation errors instead of throws.
        private static bool IsNumericPrimitive(object value)
        {
            return value is sbyte
                || value is byte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong
                || value is float
                || value is double
                || value is decimal;
        }

        // The collector already orders locals, parameters, and fields. Stopping at the first name
        // preserves that precedence instead of introducing a separate condition-specific lookup rule.
        private UloopPausePointCapturedVariableEntry FindEntry(
            IReadOnlyList<UloopPausePointCapturedVariableEntry> entries)
        {
            foreach (UloopPausePointCapturedVariableEntry entry in entries)
            {
                if (string.Equals(entry.Name, _variableName, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private UloopPausePointHitWhenEvaluation EvaluateEquality(bool valuesAreEqual)
        {
            bool matched = _operator == UloopPausePointHitWhenOperator.Equal
                ? valuesAreEqual
                : !valuesAreEqual;
            return new UloopPausePointHitWhenEvaluation(matched, string.Empty);
        }

        private UloopPausePointHitWhenEvaluation EvaluateNumber(double capturedNumber)
        {
            if (_operator == UloopPausePointHitWhenOperator.Equal)
            {
                return new UloopPausePointHitWhenEvaluation(capturedNumber == _numericLiteral, string.Empty);
            }

            if (_operator == UloopPausePointHitWhenOperator.NotEqual)
            {
                return new UloopPausePointHitWhenEvaluation(capturedNumber != _numericLiteral, string.Empty);
            }

            if (_operator == UloopPausePointHitWhenOperator.GreaterThan)
            {
                return new UloopPausePointHitWhenEvaluation(capturedNumber > _numericLiteral, string.Empty);
            }

            if (_operator == UloopPausePointHitWhenOperator.GreaterThanOrEqual)
            {
                return new UloopPausePointHitWhenEvaluation(capturedNumber >= _numericLiteral, string.Empty);
            }

            if (_operator == UloopPausePointHitWhenOperator.LessThan)
            {
                return new UloopPausePointHitWhenEvaluation(capturedNumber < _numericLiteral, string.Empty);
            }

            return new UloopPausePointHitWhenEvaluation(capturedNumber <= _numericLiteral, string.Empty);
        }

        private enum UloopPausePointHitWhenOperator
        {
            Equal,
            NotEqual,
            GreaterThan,
            GreaterThanOrEqual,
            LessThan,
            LessThanOrEqual,
        }

        private enum UloopPausePointHitWhenLiteralKind
        {
            None,
            Null,
            Boolean,
            Number,
            String,
        }
    }
}
#endif
