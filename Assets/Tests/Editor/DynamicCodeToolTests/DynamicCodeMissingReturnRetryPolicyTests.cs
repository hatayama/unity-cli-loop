using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Characterizes missing-return retry decisions and transformations before policy extraction.
    /// </summary>
    [TestFixture]
    public sealed class DynamicCodeMissingReturnRetryPolicyTests
    {
        /// <summary>
        /// Verifies compiler codes and log fallbacks identify only missing-return failures.
        /// </summary>
        [Test]
        public void LooksLikeMissingReturn_WithCompilerAndLogSignals_RecognizesSupportedPatterns()
        {
            ExecutionResult compilerResult = new()
            {
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError { ErrorCode = "CS0161" }
                }
            };
            ExecutionResult logResult = new()
            {
                Logs = new List<string> { "CS0127: Since the method returns void" }
            };
            ExecutionResult unrelatedResult = new()
            {
                Logs = new List<string> { "CS0103: The name does not exist" }
            };

            Assert.That(DynamicCodeMissingReturnRetryPolicy.LooksLikeMissingReturn(compilerResult), Is.True);
            Assert.That(DynamicCodeMissingReturnRetryPolicy.LooksLikeMissingReturn(logResult), Is.True);
            Assert.That(DynamicCodeMissingReturnRetryPolicy.LooksLikeMissingReturn(unrelatedResult), Is.False);
        }

        /// <summary>
        /// Verifies retries remain limited to script-style top-level snippets.
        /// </summary>
        [TestCase("int value = 1;", true)]
        [TestCase("namespace Sample { class Value {} }", false)]
        [TestCase("public class Value { public int Run() => 1; }", false)]
        public void CanRetryMissingReturn_WithSourceShape_ReturnsExpectedDecision(
            string code,
            bool expected)
        {
            bool actual = DynamicCodeMissingReturnRetryPolicy.CanRetryMissingReturn(code);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies return insertion preserves existing semicolons and appends the exact fallback statement.
        /// </summary>
        [TestCase("int value = 1", "int value = 1;\nreturn null;")]
        [TestCase("int value = 1;", "int value = 1;\nreturn null;")]
        [TestCase("int value = 1;  ", "int value = 1;  \nreturn null;")]
        public void AppendReturnIfMissing_WithCode_AppendsExactFallback(
            string code,
            string expected)
        {
            string actual = DynamicCodeMissingReturnRetryPolicy.AppendReturnIfMissing(code);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies original and retry logs retain their order without mutating the original list.
        /// </summary>
        [Test]
        public void MergeLogs_WithOriginalAndRetryLogs_PreservesOrderAndInputs()
        {
            List<string> originalLogs = new() { "initial-1", "initial-2" };
            List<string> retryLogs = new() { "retry-1", "retry-2" };

            List<string> mergedLogs = DynamicCodeMissingReturnRetryPolicy.MergeLogs(originalLogs, retryLogs);
            List<string> retryOnlyLogs = DynamicCodeMissingReturnRetryPolicy.MergeLogs(null, retryLogs);
            List<string> originalOnlyLogs = DynamicCodeMissingReturnRetryPolicy.MergeLogs(originalLogs, null);

            Assert.That(mergedLogs, Is.EqualTo(new[] { "initial-1", "initial-2", "retry-1", "retry-2" }));
            Assert.That(retryOnlyLogs, Is.EqualTo(retryLogs));
            Assert.That(originalOnlyLogs, Is.EqualTo(originalLogs));
            Assert.That(originalLogs, Is.EqualTo(new[] { "initial-1", "initial-2" }));
        }

        /// <summary>
        /// Verifies successful initial results bypass the retry delegate.
        /// </summary>
        [Test]
        public async Task RetryMissingReturnIfNeeded_WhenInitialResultSucceeds_DoesNotInvokeDelegate()
        {
            ExecutionResult initialResult = new() { Success = true, Result = "done" };
            int invocationCount = 0;
            Func<string, CancellationToken, Task<ExecutionResult>> executeRetryAsync = (code, ct) =>
            {
                invocationCount++;
                return Task.FromResult(new ExecutionResult { Success = true });
            };

            ExecutionResult result = await DynamicCodeMissingReturnRetryPolicy.RetryMissingReturnIfNeeded(
                initialResult,
                "return 1;",
                executeRetryAsync,
                CancellationToken.None);

            Assert.That(result, Is.SameAs(initialResult));
            Assert.That(invocationCount, Is.Zero);
        }

        /// <summary>
        /// Verifies retry execution receives appended code and failed retry logs follow original logs.
        /// </summary>
        [Test]
        public async Task RetryMissingReturnIfNeeded_WhenRetryFails_PassesCodeAndMergesLogs()
        {
            ExecutionResult initialResult = new()
            {
                Success = false,
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError { ErrorCode = "CS0161" }
                },
                Logs = new List<string> { "initial failure" }
            };
            ExecutionResult retryResult = new()
            {
                Success = false,
                Logs = new List<string> { "retry failure" }
            };
            string capturedCode = null;
            Func<string, CancellationToken, Task<ExecutionResult>> executeRetryAsync = (code, ct) =>
            {
                capturedCode = code;
                return Task.FromResult(retryResult);
            };

            ExecutionResult result = await DynamicCodeMissingReturnRetryPolicy.RetryMissingReturnIfNeeded(
                initialResult,
                "int value = 1",
                executeRetryAsync,
                CancellationToken.None);

            Assert.That(result, Is.SameAs(retryResult));
            Assert.That(capturedCode, Is.EqualTo("int value = 1;\nreturn null;"));
            Assert.That(result.Logs, Is.EqualTo(new[] { "initial failure", "retry failure" }));
        }
    }
}
