using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Editor SessionState behavior.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateRepositoryTests
    {
        private UnityCliLoopEditorSessionStateSnapshot _originalSnapshot;
        private UnityCliLoopEditorSessionStateService _sessionStateService;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSnapshot = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSnapshot.Restore(_sessionStateService);
        }

        [Test]
        public void GetFlags_WhenSessionStateIsEmpty_ReturnsFalseDefaults()
        {
            // Verifies that transient runtime flags do not opt into stale recovery by default.
            Assert.That(_sessionStateService.GetIsServerRunning(), Is.False);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.False);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.False);
        }

        [Test]
        public void GetFlags_WhenServiceAndRepositoryAreRecreated_ReadsExistingSessionValues()
        {
            // Verifies that SessionState survives service/repository recreation within the same Editor session.
            _sessionStateService.MarkDomainReloadStarted(serverIsRunning: true);

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            Assert.That(recreatedService.GetIsServerRunning(), Is.True);
            Assert.That(recreatedService.GetIsAfterCompile(), Is.True);
            Assert.That(recreatedService.GetIsDomainReloadInProgress(), Is.True);
            Assert.That(recreatedService.GetIsReconnecting(), Is.True);
            Assert.That(recreatedService.GetShowReconnectingUI(), Is.True);
            Assert.That(recreatedService.GetShowPostCompileReconnectingUI(), Is.True);
        }

        [Test]
        public void ClearAll_WhenFlagsAreSet_ClearsEveryTransientFlag()
        {
            // Verifies that test and shutdown cleanup can reset all runtime SessionState flags together.
            _sessionStateService.MarkDomainReloadStarted(serverIsRunning: true);

            _sessionStateService.ClearAll();

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.False);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.False);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.False);
        }
    }
}
