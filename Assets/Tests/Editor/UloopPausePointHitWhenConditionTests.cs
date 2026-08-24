using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies parsing and captured-variable evaluation for pause-point hit conditions.
    /// </summary>
    [TestFixture]
    public sealed class UloopPausePointHitWhenConditionTests
    {
        /// <summary>
        /// Verifies each supported operator and literal form parses into a reusable condition.
        /// </summary>
        [TestCase("speed == 1")]
        [TestCase("speed != 1")]
        [TestCase("speed > 1")]
        [TestCase("speed >= 1")]
        [TestCase("speed < 1")]
        [TestCase("speed <= 1")]
        [TestCase("value == null")]
        [TestCase("value != null")]
        [TestCase("enabled == true")]
        [TestCase("enabled != false")]
        [TestCase("label == \"alpha\"")]
        [TestCase("label != 'beta'")]
        public void Parse_WhenExpressionUsesSupportedOperatorAndLiteral_Succeeds(string expression)
        {
            UloopPausePointHitWhenParseResult result = UloopPausePointHitWhenCondition.Parse(expression);

            Assert.That(result.Condition, Is.Not.Null);
            Assert.That(result.Condition.Expression, Is.EqualTo(expression));
            Assert.That(result.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies malformed expressions explain the accepted hit-when grammar.
        /// </summary>
        [TestCase("")]
        [TestCase("speed ~~ 1")]
        [TestCase("player.speed == 1")]
        public void Parse_WhenExpressionDoesNotMatchGrammar_ReturnsGrammarError(string expression)
        {
            UloopPausePointHitWhenParseResult result = UloopPausePointHitWhenCondition.Parse(expression);

            Assert.That(result.Condition, Is.Null);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("--hit-when must use '<name> <op> <literal>' where name is an identifier or this and op is ==, !=, >, >=, <, or <=."));
        }

        /// <summary>
        /// Verifies unsupported literal text is rejected before the pause point is armed.
        /// </summary>
        [Test]
        public void Parse_WhenLiteralIsNotSupported_ReturnsLiteralError()
        {
            UloopPausePointHitWhenParseResult result = UloopPausePointHitWhenCondition.Parse("speed == fast");

            Assert.That(result.Condition, Is.Null);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("--hit-when literal must be null, true, false, an invariant number, or a quoted string."));
        }

        /// <summary>
        /// Verifies unescaped delimiter characters inside quoted literals are rejected.
        /// </summary>
        [TestCase("label == \"a\" \"b\"")]
        [TestCase("label == 'a' 'b'")]
        public void Parse_WhenQuotedLiteralContainsItsDelimiter_ReturnsLiteralError(string expression)
        {
            UloopPausePointHitWhenParseResult result = UloopPausePointHitWhenCondition.Parse(expression);

            Assert.That(result.Condition, Is.Null);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("--hit-when literal must be null, true, false, an invariant number, or a quoted string."));
        }

        /// <summary>
        /// Verifies non-numeric literals reject ordering operators at parse time.
        /// </summary>
        [TestCase("enabled > true")]
        [TestCase("label <= \"alpha\"")]
        [TestCase("value >= null")]
        public void Parse_WhenNonNumericLiteralUsesOrderingOperator_ReturnsOperatorError(string expression)
        {
            UloopPausePointHitWhenParseResult result = UloopPausePointHitWhenCondition.Parse(expression);

            Assert.That(result.Condition, Is.Null);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("--hit-when ordering operators require a numeric literal."));
        }

        /// <summary>
        /// Verifies numeric primitive values compare through invariant double promotion.
        /// </summary>
        [Test]
        public void Evaluate_WhenNumericPrimitiveValuesMeetCondition_MatchesAcrossNumericTypes()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("value >= 2.5");
            UloopPausePointHitWhenEvaluation integerEvaluation = condition.Evaluate(CreateEntries("value", 3));
            UloopPausePointHitWhenEvaluation floatEvaluation = condition.Evaluate(CreateEntries("value", 3f));
            UloopPausePointHitWhenEvaluation doubleEvaluation = condition.Evaluate(CreateEntries("value", 3d));
            UloopPausePointHitWhenEvaluation nonMatchEvaluation = condition.Evaluate(CreateEntries("value", 2));

            Assert.That(integerEvaluation.Matched, Is.True);
            Assert.That(integerEvaluation.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(floatEvaluation.Matched, Is.True);
            Assert.That(floatEvaluation.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(doubleEvaluation.Matched, Is.True);
            Assert.That(doubleEvaluation.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(nonMatchEvaluation.Matched, Is.False);
            Assert.That(nonMatchEvaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies boolean comparisons require and compare Boolean captured values.
        /// </summary>
        [Test]
        public void Evaluate_WhenBooleanValueMatchesCondition_ReturnsMatch()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("enabled != false");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("enabled", true));

            Assert.That(evaluation.Matched, Is.True);
            Assert.That(evaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies string comparisons use ordinal matching rather than case-insensitive matching.
        /// </summary>
        [Test]
        public void Evaluate_WhenStringComparisonDiffersByCase_DoesNotMatch()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("state == \"ready\"");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("state", "Ready"));

            Assert.That(evaluation.Matched, Is.False);
            Assert.That(evaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies null conditions compare only whether the captured value is null.
        /// </summary>
        [Test]
        public void Evaluate_WhenNullValueMatchesNullLiteral_ReturnsMatch()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("value == null");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("value", null));

            Assert.That(evaluation.Matched, Is.True);
            Assert.That(evaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies destroyed Unity objects use Unity fake-null semantics for null conditions.
        /// </summary>
        [Test]
        public void Evaluate_WhenUnityObjectWasDestroyed_MatchesNullLiteral()
        {
            ScriptableObject destroyedObject = ScriptableObject.CreateInstance<ScriptableObject>();
            Object.DestroyImmediate(destroyedObject);
            UloopPausePointHitWhenCondition condition = ParseCondition("value == null");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("value", destroyedObject));

            Assert.That(evaluation.Matched, Is.True);
            Assert.That(evaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies every numeric comparison operator distinguishes matching and non-matching values.
        /// </summary>
        [TestCase("value == 5", 5, true, 4, false)]
        [TestCase("value != 5", 4, true, 5, false)]
        [TestCase("value > 5", 6, true, 5, false)]
        [TestCase("value >= 5", 5, true, 4, false)]
        [TestCase("value < 5", 4, true, 5, false)]
        [TestCase("value <= 5", 5, true, 6, false)]
        public void Evaluate_WhenNumericOperatorIsUsed_ReturnsExpectedMatchStates(
            string expression,
            int matchingValue,
            bool expectedMatch,
            int nonMatchingValue,
            bool expectedNonMatch)
        {
            UloopPausePointHitWhenCondition condition = ParseCondition(expression);
            UloopPausePointHitWhenEvaluation matchingEvaluation = condition.Evaluate(CreateEntries("value", matchingValue));
            UloopPausePointHitWhenEvaluation nonMatchingEvaluation = condition.Evaluate(CreateEntries("value", nonMatchingValue));

            Assert.That(matchingEvaluation.Matched, Is.EqualTo(expectedMatch));
            Assert.That(matchingEvaluation.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(nonMatchingEvaluation.Matched, Is.EqualTo(expectedNonMatch));
            Assert.That(nonMatchingEvaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies a missing captured variable returns an evaluation error instead of throwing.
        /// </summary>
        [Test]
        public void Evaluate_WhenVariableIsMissing_ReturnsLookupError()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("value == 1");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(new List<UloopPausePointCapturedVariableEntry>());

            Assert.That(evaluation.Matched, Is.False);
            Assert.That(
                evaluation.ErrorMessage,
                Is.EqualTo("--hit-when could not find variable 'value' in the captured frame."));
        }

        /// <summary>
        /// Verifies Boolean conditions report a type error for non-Boolean captured values.
        /// </summary>
        [Test]
        public void Evaluate_WhenBooleanConditionReceivesNonBooleanValue_ReturnsTypeError()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("enabled == true");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("enabled", "true"));

            Assert.That(evaluation.Matched, Is.False);
            Assert.That(
                evaluation.ErrorMessage,
                Is.EqualTo("--hit-when expected variable 'enabled' to be Boolean."));
        }

        /// <summary>
        /// Verifies numeric conditions reject non-numeric primitive values such as char.
        /// </summary>
        [Test]
        public void Evaluate_WhenNumericConditionReceivesChar_ReturnsTypeError()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("value > 1");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("value", '1'));

            Assert.That(evaluation.Matched, Is.False);
            Assert.That(
                evaluation.ErrorMessage,
                Is.EqualTo("--hit-when expected variable 'value' to be a numeric primitive."));
        }

        /// <summary>
        /// Verifies string conditions report a type error for non-string captured values.
        /// </summary>
        [Test]
        public void Evaluate_WhenStringConditionReceivesNonStringValue_ReturnsTypeError()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("state == \"ready\"");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("state", 1));

            Assert.That(evaluation.Matched, Is.False);
            Assert.That(
                evaluation.ErrorMessage,
                Is.EqualTo("--hit-when expected variable 'state' to be String."));
        }

        /// <summary>
        /// Verifies the synthetic this entry is available as a valid condition name.
        /// </summary>
        [Test]
        public void Evaluate_WhenThisEntryMatchesNullLiteral_ReturnsMatch()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("this == null");
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(CreateEntries("this", null));

            Assert.That(evaluation.Matched, Is.True);
            Assert.That(evaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies duplicate names use the first captured entry, preserving collector precedence.
        /// </summary>
        [Test]
        public void Evaluate_WhenVariableNameIsDuplicated_UsesFirstCapturedEntry()
        {
            UloopPausePointHitWhenCondition condition = ParseCondition("score == 1");
            List<UloopPausePointCapturedVariableEntry> entries = new List<UloopPausePointCapturedVariableEntry>
            {
                new UloopPausePointCapturedVariableEntry("score", UloopCapturedVariableScope.Local, 1),
                new UloopPausePointCapturedVariableEntry("score", UloopCapturedVariableScope.InstanceField, 2),
            };
            UloopPausePointHitWhenEvaluation evaluation = condition.Evaluate(entries);

            Assert.That(evaluation.Matched, Is.True);
            Assert.That(evaluation.ErrorMessage, Is.EqualTo(string.Empty));
        }

        // Keeps test setup concise while preserving the same local scope used by collected variables.
        private static List<UloopPausePointCapturedVariableEntry> CreateEntries(string name, object value)
        {
            return new List<UloopPausePointCapturedVariableEntry>
            {
                new UloopPausePointCapturedVariableEntry(name, UloopCapturedVariableScope.Local, value),
            };
        }

        // Fails immediately when a parser test accidentally exercises an invalid expression.
        private static UloopPausePointHitWhenCondition ParseCondition(string expression)
        {
            UloopPausePointHitWhenParseResult result = UloopPausePointHitWhenCondition.Parse(expression);

            Assert.That(result.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(result.Condition, Is.Not.Null);
            return result.Condition;
        }
    }
}
