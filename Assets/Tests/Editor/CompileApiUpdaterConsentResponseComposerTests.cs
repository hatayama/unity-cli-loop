using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests Warning and NextActions composition for a declined Script Updating Consent dialog.
    /// </summary>
    [TestFixture]
    public sealed class CompileApiUpdaterConsentResponseComposerTests
    {
        private const string WarningText =
            "Unity's API Updater requested consent to rewrite source files ('Script Updating Consent' dialog). uloop declines this automatically: source files are not rewritten without explicit user consent. The obsolete-API compile errors it would have fixed are reported in Errors.";

        private const string NextActionText =
            "Fix the obsolete API usages reported in Errors, or ask the user to accept the Script Updating Consent dialog in an interactive Unity session.";

        /// <summary>
        /// What: a declined compile with empty Warning and NextActions gets the fixed literals only.
        /// </summary>
        [Test]
        public void Apply_WhenWarningAndNextActionsAreEmpty_AssignsFixedLiterals()
        {
            CompileResponse response = CreateEmptyResponse();

            CompileApiUpdaterConsentResponseComposer.Apply(response, apiUpdaterConsentDeclined: true);

            Assert.That(response.Warning, Is.EqualTo(WarningText));
            Assert.That(response.NextActions, Is.EqualTo(new[] { NextActionText }));
        }

        /// <summary>
        /// What: an existing Warning is kept and the API Updater warning is appended after a newline.
        /// </summary>
        [Test]
        public void Apply_WhenWarningAlreadyExists_AppendsFixedWarning()
        {
            CompileResponse response = CreateEmptyResponse();
            response.Warning = "Play Mode was active with 2 enabled pause point(s).";

            CompileApiUpdaterConsentResponseComposer.Apply(response, apiUpdaterConsentDeclined: true);

            Assert.That(
                response.Warning,
                Is.EqualTo("Play Mode was active with 2 enabled pause point(s).\n" + WarningText));
            Assert.That(response.NextActions, Is.EqualTo(new[] { NextActionText }));
        }

        /// <summary>
        /// What: existing NextActions are kept and the API Updater action is appended at the end.
        /// </summary>
        [Test]
        public void Apply_WhenNextActionsAlreadyExist_AppendsFixedNextAction()
        {
            CompileResponse response = CreateEmptyResponse();
            response.NextActions = new[] { "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result." };

            CompileApiUpdaterConsentResponseComposer.Apply(response, apiUpdaterConsentDeclined: true);

            Assert.That(response.Warning, Is.EqualTo(WarningText));
            Assert.That(
                response.NextActions,
                Is.EqualTo(new[]
                {
                    "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result.",
                    NextActionText
                }));
        }

        /// <summary>
        /// What: existing Warning and NextActions are both preserved and the API Updater lines are appended.
        /// </summary>
        [Test]
        public void Apply_WhenWarningAndNextActionsAlreadyExist_AppendsBoth()
        {
            CompileResponse response = CreateEmptyResponse();
            response.Warning = "Play Mode was active with 2 enabled pause point(s).";
            response.NextActions = new[] { "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result." };

            CompileApiUpdaterConsentResponseComposer.Apply(response, apiUpdaterConsentDeclined: true);

            Assert.That(
                response.Warning,
                Is.EqualTo("Play Mode was active with 2 enabled pause point(s).\n" + WarningText));
            Assert.That(
                response.NextActions,
                Is.EqualTo(new[]
                {
                    "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result.",
                    NextActionText
                }));
        }

        /// <summary>
        /// What: a compile that did not decline leaves Warning and NextActions unchanged.
        /// </summary>
        [Test]
        public void Apply_WhenNotDeclined_LeavesResponseUnchanged()
        {
            CompileResponse response = CreateEmptyResponse();
            response.Warning = "existing";
            response.NextActions = new[] { "keep" };

            CompileApiUpdaterConsentResponseComposer.Apply(response, apiUpdaterConsentDeclined: false);

            Assert.That(response.Warning, Is.EqualTo("existing"));
            Assert.That(response.NextActions, Is.EqualTo(new[] { "keep" }));
        }

        private static CompileResponse CreateEmptyResponse()
        {
            return new CompileResponse(
                success: true,
                errorCount: 0,
                warningCount: 0,
                errors: null,
                warnings: null,
                message: null);
        }
    }
}
