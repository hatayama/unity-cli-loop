using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the pure intercept decision for Unity's Script Updating Consent dialog.
    /// </summary>
    [TestFixture]
    public sealed class CompileApiUpdaterConsentDecisionTests
    {
        /// <summary>
        /// What: an in-flight CLI compile is required before the dialog is intercepted.
        /// </summary>
        [Test]
        public void Decide_WhenCliCompileIsNotInFlight_DoesNotIntercept()
        {
            (bool intercept, int declinedResult) decision = CompileApiUpdaterConsentDecision.Decide(
                isCliCompileInFlight: false,
                title: "Script Updating Consent");

            Assert.That(decision.intercept, Is.False);
            Assert.That(decision.declinedResult, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a different dialog title is left to Unity even during a CLI compile.
        /// </summary>
        [Test]
        public void Decide_WhenTitleDoesNotMatch_DoesNotIntercept()
        {
            (bool intercept, int declinedResult) decision = CompileApiUpdaterConsentDecision.Decide(
                isCliCompileInFlight: true,
                title: "API Update Required");

            Assert.That(decision.intercept, Is.False);
            Assert.That(decision.declinedResult, Is.EqualTo(0));
        }

        /// <summary>
        /// What: in-flight plus the Script Updating Consent title declines with result 1 (No).
        /// </summary>
        [Test]
        public void Decide_WhenInFlightAndTitleMatches_InterceptsWithDeclinedResult()
        {
            (bool intercept, int declinedResult) decision = CompileApiUpdaterConsentDecision.Decide(
                isCliCompileInFlight: true,
                title: "Script Updating Consent");

            Assert.That(decision.intercept, Is.True);
            Assert.That(decision.declinedResult, Is.EqualTo(1));
        }
    }
}
