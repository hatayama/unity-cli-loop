using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the focused-editor warnings are limited to the affected observations.
    /// </summary>
    public sealed class EditorUnfocusedWarningBuilderTests
    {
        /// <summary>
        /// What: an unobserved keyboard press while the Editor is unfocused returns the exact recovery warning.
        /// </summary>
        [Test]
        public void BuildKeyboardInputWarning_WhenPressEdgeIsFalseAndEditorIsUnfocused_ReturnsExactWarning()
        {
            string warning = EditorUnfocusedWarningBuilder.BuildKeyboardInputWarning(
                isEditorFocused: false,
                isPressAction: true,
                pressEdgeObserved: false,
                isSuccessful: true);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Keyboard input was queued while the Unity Editor was unfocused, so the press edge was not observed. Run `uloop focus-window` before retrying; queued input may be delivered all at once when the Editor regains focus."));
        }

        /// <summary>
        /// What: a successful press with an omitted edge observation while the Editor is unfocused returns the exact recovery warning.
        /// </summary>
        [Test]
        public void BuildKeyboardInputWarning_WhenPressEdgeIsNullAndEditorIsUnfocused_ReturnsExactWarning()
        {
            string warning = EditorUnfocusedWarningBuilder.BuildKeyboardInputWarning(
                isEditorFocused: false,
                isPressAction: true,
                pressEdgeObserved: null,
                isSuccessful: true);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Keyboard input was queued while the Unity Editor was unfocused, so the press edge was not observed. Run `uloop focus-window` before retrying; queued input may be delivered all at once when the Editor regains focus."));
        }

        /// <summary>
        /// What: a focused Editor does not warn about an otherwise unobserved press edge.
        /// </summary>
        [Test]
        public void BuildKeyboardInputWarning_WhenEditorIsFocused_ReturnsEmpty()
        {
            string warning = EditorUnfocusedWarningBuilder.BuildKeyboardInputWarning(
                isEditorFocused: true,
                isPressAction: true,
                pressEdgeObserved: false,
                isSuccessful: true);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a KeyUp-style action does not warn when its edge observation is contractually omitted.
        /// </summary>
        [Test]
        public void BuildKeyboardInputWarning_WhenActionHasNoPressEdge_ReturnsEmpty()
        {
            string warning = EditorUnfocusedWarningBuilder.BuildKeyboardInputWarning(
                isEditorFocused: false,
                isPressAction: false,
                pressEdgeObserved: null,
                isSuccessful: true);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a timed-out keyboard action does not claim that an omitted observation was caused by focus.
        /// </summary>
        [Test]
        public void BuildKeyboardInputWarning_WhenActionIsNotSuccessful_ReturnsEmpty()
        {
            string warning = EditorUnfocusedWarningBuilder.BuildKeyboardInputWarning(
                isEditorFocused: false,
                isPressAction: true,
                pressEdgeObserved: null,
                isSuccessful: false);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: an unfocused Editor in Play Mode returns the exact progress hint.
        /// </summary>
        [Test]
        public void BuildPlayModeProgressHint_WhenPlayingAndEditorIsUnfocused_ReturnsExactHint()
        {
            string hint = EditorUnfocusedWarningBuilder.BuildPlayModeProgressHint(
                isPlaying: true,
                isEditorFocused: false);

            Assert.That(
                hint,
                Is.EqualTo(
                    "The Unity Editor is unfocused while Play Mode is running, so Play Mode progress may be throttled. Run `uloop focus-window`, or use the `pause-point --await`/`--trigger` flow instead of polling for progress."));
        }

        /// <summary>
        /// What: Edit Mode does not emit a Play Mode progress hint.
        /// </summary>
        [Test]
        public void BuildPlayModeProgressHint_WhenNotPlaying_ReturnsEmpty()
        {
            string hint = EditorUnfocusedWarningBuilder.BuildPlayModeProgressHint(
                isPlaying: false,
                isEditorFocused: false);

            Assert.That(hint, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a focused Editor does not emit a Play Mode progress hint.
        /// </summary>
        [Test]
        public void BuildPlayModeProgressHint_WhenEditorIsFocused_ReturnsEmpty()
        {
            string hint = EditorUnfocusedWarningBuilder.BuildPlayModeProgressHint(
                isPlaying: true,
                isEditorFocused: true);

            Assert.That(hint, Is.EqualTo(string.Empty));
        }
    }
}
