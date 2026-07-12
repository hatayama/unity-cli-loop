using System;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies user-code evaluation failures become explicit watch history results.
    /// </summary>
    [TestFixture]
    public sealed class WatchExpressionEvaluationTests
    {
        /// <summary>
        /// Verifies an exception from the generated evaluator is returned as a typed failure result.
        /// </summary>
        [Test]
        public void Evaluate_WhenUserCodeThrows_ReturnsFailureResult()
        {
            MethodInfo method = typeof(ThrowingWatchExpression).GetMethod(nameof(ThrowingWatchExpression.Evaluate));
            CompiledWatchExpressionEvaluator evaluator = new(
                new ThrowingWatchExpression(),
                method);

            WatchEvaluationResult result = evaluator.Evaluate();

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorTypeName, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(result.ErrorMessage, Is.EqualTo("watch failed"));
        }

        private sealed class ThrowingWatchExpression
        {
            public object Evaluate()
            {
                throw new InvalidOperationException("watch failed");
            }
        }
    }
}
