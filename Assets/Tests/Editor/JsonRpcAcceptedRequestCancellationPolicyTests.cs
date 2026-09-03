using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests accepted-request cancellation decisions before infrastructure parses transport details.
    /// </summary>
    public sealed class JsonRpcAcceptedRequestCancellationPolicyTests
    {
        [Test]
        public void ShouldCancelOnClientDisconnect_WhenMethodIsNotCompile_ReturnsTrue()
        {
            // Verifies non-compile accepted work is tied to the CLI connection lifetime.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_GET_LOGS,
                true,
                false);

            Assert.That(shouldCancel, Is.True);
        }

        [Test]
        public void ShouldCancelOnClientDisconnect_WhenCompileWaitsForDomainReload_ReturnsFalse()
        {
            // Verifies long compile requests can persist after the CLI connection closes.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_COMPILE,
                true,
                false);

            Assert.That(shouldCancel, Is.False);
        }

        [Test]
        public void ShouldCancelOnClientDisconnect_WhenCompileDoesNotWaitForDomainReload_ReturnsTrue()
        {
            // Verifies fire-and-forget compile requests keep the usual disconnect cancellation behavior.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_COMPILE,
                false,
                false);

            Assert.That(shouldCancel, Is.True);
        }

        [Test]
        public void ShouldCancelOnClientDisconnect_WhenCompileWaitPreferenceIsUnknown_ReturnsFalse()
        {
            // Verifies missing, null, or non-boolean transport values preserve the compile default wait contract.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_COMPILE,
                null,
                false);

            Assert.That(shouldCancel, Is.False);
        }

        /// <summary>
        /// Verifies respect-path PlayMode run-tests keeps running after the CLI disconnects.
        /// </summary>
        [Test]
        public void ShouldCancelOnClientDisconnect_WhenRunTestsRespectsEnterPlayModeSettings_ReturnsFalse()
        {
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                null,
                true);

            Assert.That(shouldCancel, Is.False);
        }

        /// <summary>
        /// Verifies default run-tests requests still cancel when the CLI disconnects.
        /// </summary>
        [Test]
        public void ShouldCancelOnClientDisconnect_WhenRunTestsDoesNotRespectEnterPlayModeSettings_ReturnsTrue()
        {
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                null,
                false);

            Assert.That(shouldCancel, Is.True);
        }
    }
}
