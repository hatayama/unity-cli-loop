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
                true);

            Assert.That(shouldCancel, Is.True);
        }

        [Test]
        public void ShouldCancelOnClientDisconnect_WhenCompileWaitsForDomainReload_ReturnsFalse()
        {
            // Verifies long compile requests can persist after the CLI connection closes.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_COMPILE,
                true);

            Assert.That(shouldCancel, Is.False);
        }

        [Test]
        public void ShouldCancelOnClientDisconnect_WhenCompileDoesNotWaitForDomainReload_ReturnsTrue()
        {
            // Verifies fire-and-forget compile requests keep the usual disconnect cancellation behavior.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_COMPILE,
                false);

            Assert.That(shouldCancel, Is.True);
        }

        [Test]
        public void ShouldCancelOnClientDisconnect_WhenCompileWaitPreferenceIsUnknown_ReturnsFalse()
        {
            // Verifies missing, null, or non-boolean transport values preserve the compile default wait contract.
            bool shouldCancel = JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                UnityCliLoopConstants.TOOL_NAME_COMPILE,
                null);

            Assert.That(shouldCancel, Is.False);
        }
    }
}
