using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    /// <summary>
    /// Pure unit coverage for run-tests CancelAfter timeout helpers.
    /// </summary>
    [TestFixture]
    public class RunTestsExecutionTimeoutTests
    {
        [Test]
        public void TryValidate_WhenTimeoutSecondsIsZero_ShouldReject()
        {
            // Verifies non-positive TimeoutSeconds fail before CancelAfter is armed.
            bool isValid = RunTestsExecutionTimeout.TryValidate(0, out string errorMessage);

            Assert.That(isValid, Is.False);
            Assert.That(errorMessage, Does.Contain("greater than zero"));
            Assert.That(errorMessage, Does.Contain("--timeout-seconds"));
        }

        [Test]
        public void TryValidate_WhenTimeoutSecondsExceedsMax_ShouldReject()
        {
            // Verifies values above MaxTimeoutSeconds are rejected so C# CancelAfter stays ahead of the CLI 30-minute absolute limit.
            int tooLarge = RunTestsExecutionTimeout.MaxTimeoutSeconds + 1;

            bool isValid = RunTestsExecutionTimeout.TryValidate(tooLarge, out string errorMessage);

            Assert.That(isValid, Is.False);
            Assert.That(errorMessage, Does.Contain(RunTestsExecutionTimeout.MaxTimeoutSeconds.ToString()));
            Assert.That(errorMessage, Does.Contain(tooLarge.ToString()));
        }

        [Test]
        public void TryValidate_WhenTimeoutSecondsIsDefault_ShouldAccept()
        {
            // Verifies the agreed default (600s) is inside the allowed range.
            bool isValid = RunTestsExecutionTimeout.TryValidate(
                RunTestsExecutionTimeout.DefaultTimeoutSeconds,
                out string errorMessage);

            Assert.That(isValid, Is.True);
            Assert.That(errorMessage, Is.Null);
        }

        [Test]
        public void TryValidate_WhenTimeoutSecondsIsMax_ShouldAccept()
        {
            // Verifies the inclusive upper bound remains usable for long suites.
            bool isValid = RunTestsExecutionTimeout.TryValidate(
                RunTestsExecutionTimeout.MaxTimeoutSeconds,
                out string errorMessage);

            Assert.That(isValid, Is.True);
            Assert.That(errorMessage, Is.Null);
        }

        [Test]
        public void CreateTimeoutMessage_ShouldGuideCallerToExtendTimeoutAndWarnAboutBackgroundRunner()
        {
            // Verifies timeout copy tells agents how to extend the budget and that Test Runner may still be running.
            string message = RunTestsExecutionTimeout.CreateTimeoutMessage(600);

            Assert.That(message, Does.Contain("600"));
            Assert.That(message, Does.Contain("--timeout-seconds"));
            Assert.That(message, Does.Contain("background"));
            Assert.That(message, Does.Contain("uloop launch -r"));
        }

        [Test]
        public void IsTimeoutCancellation_WhenParentIsNotCanceled_ShouldReturnTrue()
        {
            // Verifies CancelAfter-driven cancellation is distinguished from parent/disconnect cancellation.
            using CancellationTokenSource parent = new();

            Assert.That(RunTestsExecutionTimeout.IsTimeoutCancellation(parent.Token), Is.True);
        }

        [Test]
        public void IsTimeoutCancellation_WhenParentIsCanceled_ShouldReturnFalse()
        {
            // Verifies parent/disconnect cancellation is not reported as a run-tests timeout.
            using CancellationTokenSource parent = new();
            parent.Cancel();

            Assert.That(RunTestsExecutionTimeout.IsTimeoutCancellation(parent.Token), Is.False);
        }

        [Test]
        public async Task CreateLinkedTimeoutSource_WhenTimeoutElapses_ShouldCancelWithoutCancelingParent()
        {
            // Verifies CancelAfter fires on the linked source while leaving the parent token usable.
            using CancellationTokenSource parent = new();
            using CancellationTokenSource linked = RunTestsExecutionTimeout.CreateLinkedTimeoutSource(
                parent.Token,
                timeoutSeconds: 1);

            TaskCompletionSource<bool> canceled =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = linked.Token.Register(() => canceled.TrySetResult(true));

            bool observed = await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(observed, Is.True);
            Assert.That(linked.IsCancellationRequested, Is.True);
            Assert.That(parent.IsCancellationRequested, Is.False);
            Assert.That(RunTestsExecutionTimeout.IsTimeoutCancellation(parent.Token), Is.True);
        }

        [Test]
        public void CreateLinkedTimeoutSource_WhenParentCancels_ShouldCancelLinkedSource()
        {
            // Verifies parent cancellation still propagates through the linked timeout source.
            using CancellationTokenSource parent = new();
            using CancellationTokenSource linked = RunTestsExecutionTimeout.CreateLinkedTimeoutSource(
                parent.Token,
                timeoutSeconds: RunTestsExecutionTimeout.DefaultTimeoutSeconds);

            parent.Cancel();

            Assert.That(linked.IsCancellationRequested, Is.True);
            Assert.That(RunTestsExecutionTimeout.IsTimeoutCancellation(parent.Token), Is.False);
        }
    }
}
