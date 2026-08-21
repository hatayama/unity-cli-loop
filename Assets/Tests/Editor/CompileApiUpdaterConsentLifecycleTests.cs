using System;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that every CompileController exit path clears the CLI-compile in-flight flag.
    /// </summary>
    [TestFixture]
    public sealed class CompileApiUpdaterConsentLifecycleTests
    {
        [TearDown]
        public void TearDown()
        {
            CompileApiUpdaterConsentState.EndCliCompile();
        }

        /// <summary>
        /// What: the normal completion path leaves the in-flight flag false.
        /// </summary>
        [Test]
        public void CompleteCompileRequest_WhenCompileFinishes_ClearsInFlight()
        {
            using CompileController controller = CreateController();
            CompileApiUpdaterConsentState.BeginCliCompile();

            controller.CompleteCompileRequestForTesting(CreateSuccessfulResult());

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: failing before RequestScriptCompilation transfers the task still clears the flag.
        /// </summary>
        [Test]
        public void ClearUntransferredCompileState_WhenRequestDoesNotStart_ClearsInFlight()
        {
            using CompileController controller = CreateController();
            CompileApiUpdaterConsentState.BeginCliCompile();

            controller.ClearUntransferredCompileStateForTesting();

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: recovery and watchdog abort share CompleteCompileRequest and clear the flag.
        /// </summary>
        [Test]
        public void CompleteCompileRequest_WhenWatchdogAborts_ClearsInFlight()
        {
            using CompileController controller = CreateController();
            CompileApiUpdaterConsentState.BeginCliCompile();
            CompileResult abortResult = new CompileResult(
                success: false,
                errorCount: 0,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: Array.Empty<CompilerMessage>(),
                warnings: Array.Empty<CompilerMessage>(),
                isIndeterminate: true,
                message: "Compilation watchdog failed unexpectedly.");

            controller.CompleteCompileRequestForTesting(abortResult);

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: cancellation completes through CompleteCompileRequest and clears the flag.
        /// </summary>
        [Test]
        public void CompleteCompileRequest_WhenCancelled_ClearsInFlight()
        {
            using CompileController controller = CreateController();
            CompileApiUpdaterConsentState.BeginCliCompile();
            CompileResult cancelledResult = new CompileResult(
                success: false,
                errorCount: 0,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: Array.Empty<CompilerMessage>(),
                warnings: Array.Empty<CompilerMessage>(),
                isIndeterminate: true,
                message: "Compilation was cancelled.");

            controller.CompleteCompileRequestForTesting(cancelledResult);

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: Cleanup clears the in-flight flag without waiting for a compile callback.
        /// </summary>
        [Test]
        public void Cleanup_WhenCliCompileIsInFlight_ClearsInFlight()
        {
            CompileController controller = CreateController();
            CompileApiUpdaterConsentState.BeginCliCompile();

            controller.Cleanup();

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
            controller.Dispose();
        }

        /// <summary>
        /// What: Dispose clears the in-flight flag through Cleanup.
        /// </summary>
        [Test]
        public void Dispose_WhenCliCompileIsInFlight_ClearsInFlight()
        {
            CompileController controller = CreateController();
            CompileApiUpdaterConsentState.BeginCliCompile();

            controller.Dispose();

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        private static CompileController CreateController()
        {
            return new CompileController(
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository(),
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository());
        }

        private static CompileResult CreateSuccessfulResult()
        {
            return new CompileResult(
                success: true,
                errorCount: 0,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: Array.Empty<CompilerMessage>(),
                warnings: Array.Empty<CompilerMessage>());
        }
    }
}
