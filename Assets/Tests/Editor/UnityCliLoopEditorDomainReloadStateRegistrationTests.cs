using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies editor startup registers the in-memory domain reload state provider.
    /// </summary>
    public sealed class UnityCliLoopEditorDomainReloadStateRegistrationTests
    {
        private IDomainReloadStateProvider _previousProvider;
        private bool _previousInProgress;

        [SetUp]
        public void SetUp()
        {
            _previousInProgress = new UnityCliLoopEditorDomainReloadStateProvider().IsDomainReloadInProgress();
            _previousProvider = DomainReloadStateRegistry.SwapProviderForTests(null);
        }

        [TearDown]
        public void TearDown()
        {
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(_previousInProgress);
            DomainReloadStateRegistry.SwapProviderForTests(_previousProvider);
        }

        /// <summary>
        /// Verifies that after startup registration the registry reports the editor's in-memory reload flag.
        /// </summary>
        [Test]
        public void RegisterForEditorStartup_WhenReloadFlagIsSet_RegistryReportsReloadInProgress()
        {
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(true);

            UnityCliLoopEditorDomainReloadStateRegistration.RegisterForEditorStartup();

            Assert.That(DomainReloadStateRegistry.IsDomainReloadInProgress(), Is.True);
            Assert.That(
                DomainReloadStateRegistry.SwapProviderForTests(null),
                Is.InstanceOf<UnityCliLoopEditorDomainReloadStateProvider>());
        }
    }
}
