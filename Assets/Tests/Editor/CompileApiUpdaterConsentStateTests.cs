using System;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the static CLI-compile in-flight gate and decline transfer onto CompileResult.
    /// </summary>
    [TestFixture]
    public sealed class CompileApiUpdaterConsentStateTests
    {
        [TearDown]
        public void TearDown()
        {
            CompileApiUpdaterConsentState.EndCliCompile();
        }

        /// <summary>
        /// What: Begin marks the compile in flight and End clears it.
        /// </summary>
        [Test]
        public void BeginAndEnd_ToggleInFlightFlag()
        {
            CompileApiUpdaterConsentState.BeginCliCompile();
            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.True);

            CompileApiUpdaterConsentState.EndCliCompile();
            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: multiple declines still copy onto the result as a single declined fact.
        /// </summary>
        [Test]
        public void AttachDeclined_WhenMarkedMultipleTimes_CopiesOnceOntoResult()
        {
            CompileApiUpdaterConsentState.BeginCliCompile();
            CompileApiUpdaterConsentState.MarkDeclined();
            CompileApiUpdaterConsentState.MarkDeclined();
            CompileResult result = CreateSuccessfulResult();

            CompileResult attached = CompileApiUpdaterConsentState.AttachDeclined(result);

            Assert.That(attached.ApiUpdaterConsentDeclined, Is.True);
            Assert.That(result.ApiUpdaterConsentDeclined, Is.False);
        }

        /// <summary>
        /// What: End clears a decline that was never copied onto a result.
        /// </summary>
        [Test]
        public void EndCliCompile_ClearsUnconsumedDecline()
        {
            CompileApiUpdaterConsentState.BeginCliCompile();
            CompileApiUpdaterConsentState.MarkDeclined();
            CompileApiUpdaterConsentState.EndCliCompile();

            CompileResult attached = CompileApiUpdaterConsentState.AttachDeclined(CreateSuccessfulResult());

            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
            Assert.That(attached.ApiUpdaterConsentDeclined, Is.False);
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
