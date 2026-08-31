namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds response warnings for operations affected by an unfocused Unity Editor.
    /// </summary>
    public static class EditorUnfocusedWarningBuilder
    {
        private const string KeyboardInputWarning =
            "Keyboard input was queued while the Unity Editor was unfocused, so the press edge was not observed. Run `uloop focus-window` before retrying; queued input may be delivered all at once when the Editor regains focus.";
        private const string PlayModeProgressHint =
            "The Unity Editor is unfocused while Play Mode is running, so Play Mode progress may be throttled. Run `uloop focus-window`, or use the `pause-point --await`/`--trigger` flow instead of polling for progress.";

        /// <summary>
        /// Builds the keyboard warning when a successful press action lacks an observed edge while unfocused.
        /// </summary>
        public static string BuildKeyboardInputWarning(
            bool isEditorFocused,
            bool isPressAction,
            bool? pressEdgeObserved,
            bool isSuccessful)
        {
            if (isEditorFocused || !isPressAction || pressEdgeObserved == true || !isSuccessful)
            {
                return string.Empty;
            }

            return KeyboardInputWarning;
        }

        /// <summary>
        /// Builds the Play Mode progress hint when the Editor is unfocused during Play Mode.
        /// </summary>
        public static string BuildPlayModeProgressHint(bool isPlaying, bool isEditorFocused)
        {
            if (!isPlaying || isEditorFocused)
            {
                return string.Empty;
            }

            return PlayModeProgressHint;
        }
    }
}
