using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies ExecuteDynamicCodePauseStateResolver behavior.
    /// </summary>
    [TestFixture]
    public class ExecuteDynamicCodePauseStateResolverTests
    {
        [Test]
        public void Resolve_WhenEditorIsNotPaused_ReturnsFalseAndEmptyIdRegardlessOfRegistryValue()
        {
            // Tests that a stale or racy non-empty registry id never leaks out while the real
            // Editor reports itself as unpaused (the up-to-one-frame external-unpause window).
            (bool editorPaused, string activePausePointId) = ExecuteDynamicCodePauseStateResolver.Resolve(
                editorIsPaused: false, registryActivePausePointId: "jump");

            Assert.That(editorPaused, Is.False);
            Assert.That(activePausePointId, Is.Empty);
        }

        [Test]
        public void Resolve_WhenEditorIsPausedByAPausePoint_ReturnsTrueAndThatMarkerId()
        {
            // Tests the common case: a pause point hit paused the Editor, so the response should
            // surface both the paused flag and which marker caused it.
            (bool editorPaused, string activePausePointId) = ExecuteDynamicCodePauseStateResolver.Resolve(
                editorIsPaused: true, registryActivePausePointId: "jump");

            Assert.That(editorPaused, Is.True);
            Assert.That(activePausePointId, Is.EqualTo("jump"));
        }

        [Test]
        public void Resolve_WhenEditorIsPausedForAnUnrelatedReason_ReturnsTrueAndEmptyId()
        {
            // Tests that a manual pause (not caused by any pause point) still reports
            // EditorPaused=true, with ActivePausePointId left empty since the registry has no
            // active freeze window in that case.
            (bool editorPaused, string activePausePointId) = ExecuteDynamicCodePauseStateResolver.Resolve(
                editorIsPaused: true, registryActivePausePointId: string.Empty);

            Assert.That(editorPaused, Is.True);
            Assert.That(activePausePointId, Is.Empty);
        }
    }
}
