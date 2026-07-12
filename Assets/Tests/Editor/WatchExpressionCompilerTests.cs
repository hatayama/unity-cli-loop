using System.Collections;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies watch expressions compile through the shared dynamic-code compiler without executing user code.
    /// </summary>
    [TestFixture]
    public sealed class WatchExpressionCompilerTests
    {
        /// <summary>
        /// Verifies a valid expression reaches the compile-only path and produces an evaluator.
        /// </summary>
        [UnityTest]
        public IEnumerator CompileAsync_ValidExpressionReturnsCompiledEvaluator()
        {
            WatchExpressionCompiler compiler = new(new DynamicCodeCompiler());
            Task<WatchCompilationResult> compilationTask = compiler.CompileAsync("1 + 2", CancellationToken.None);

            while (!compilationTask.IsCompleted)
            {
                yield return null;
            }

            Assert.That(compilationTask.IsCompletedSuccessfully, Is.True);
            Assert.That(compilationTask.Result.Success, Is.True);
            Assert.That(compilationTask.Result.Evaluator, Is.Not.Null);
        }

        /// <summary>
        /// Verifies an empty expression is rejected before invoking the compiler.
        /// </summary>
        [Test]
        public void CompileAsync_EmptyExpressionReturnsFailure()
        {
            WatchExpressionCompiler compiler = new(new DynamicCodeCompiler());

            Task<WatchCompilationResult> compilationTask = compiler.CompileAsync("", CancellationToken.None);

            Assert.That(compilationTask.IsCompletedSuccessfully, Is.True);
            Assert.That(compilationTask.Result.Success, Is.False);
            Assert.That(compilationTask.Result.ErrorMessage, Does.Contain("must not be empty"));
        }
    }
}
