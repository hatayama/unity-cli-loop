using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers when an apply response asks the CLI to wait for the Editor to settle and apply the
    /// same request again: only when every failure of the run is the Editor compiling or importing.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadEditorReadyRetryTests
    {
        /// <summary>
        /// What: a run with no failure has nothing to apply again.
        /// </summary>
        [Test]
        public void Decide_WithNoFailure_IsFalse()
        {
            Assert.That(HotReloadEditorReadyRetry.Decide(false, HotReloadFailureKinds.None), Is.False);
        }

        /// <summary>
        /// What: a run refused only because the Editor was compiling or importing asks for the retry.
        /// </summary>
        [Test]
        public void Decide_WithOnlyEditorNotReady_IsTrue()
        {
            Assert.That(HotReloadEditorReadyRetry.Decide(true, HotReloadFailureKinds.EditorNotReady), Is.True);
        }

        /// <summary>
        /// What: a failure the reader has to fix next to the busy Editor gives the same result after
        /// a wait, so the run does not ask for the retry.
        /// </summary>
        [Test]
        public void Decide_WithEditorNotReadyAndDeclaration_IsFalse()
        {
            Assert.That(
                HotReloadEditorReadyRetry.Decide(
                    true,
                    HotReloadFailureKinds.EditorNotReady | HotReloadFailureKinds.Declaration),
                Is.False);
        }

        /// <summary>
        /// What: a failure the reader has to fix alone does not ask for the retry.
        /// </summary>
        [Test]
        public void Decide_WithOnlyDeclaration_IsFalse()
        {
            Assert.That(HotReloadEditorReadyRetry.Decide(true, HotReloadFailureKinds.Declaration), Is.False);
        }

        /// <summary>
        /// What: a missing compiled assembly needs a compile, which waiting does not run, so the run
        /// does not ask for the retry even when the Editor was busy too.
        /// </summary>
        [Test]
        public void Decide_WithEditorNotReadyAndCompiledAssemblyMissing_IsFalse()
        {
            Assert.That(
                HotReloadEditorReadyRetry.Decide(
                    true,
                    HotReloadFailureKinds.EditorNotReady | HotReloadFailureKinds.CompiledAssemblyMissing),
                Is.False);
        }

        /// <summary>
        /// What: a failure that lost its kind is treated as one the reader fixes, so it does not ask
        /// for the retry.
        /// </summary>
        [Test]
        public void Decide_WithAFailureOfNoKind_IsFalse()
        {
            Assert.That(HotReloadEditorReadyRetry.Decide(true, HotReloadFailureKinds.None), Is.False);
        }
    }
}
