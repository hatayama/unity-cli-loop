using System.Collections.Generic;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.InternalAPIBridge
{
    /// <summary>
    /// Switches PlayModeView.enterPlayModeBehavior between PlayFocused and PlayUnfocused.
    /// Why: EditorApplicationLayout raises the Editor window above other apps on Play and on
    /// resume-from-pause unless the play-mode view is PlayUnfocused, so background suppression
    /// must flip PlayFocused views while the Editor is unfocused. Views set to PlayMaximized or
    /// PlayUnfocused by the user are never touched by the suppress direction.
    /// </summary>
    public static class PlayModeViewFocusBridge
    {
        /// <summary>
        /// Forces every PlayFocused view to PlayUnfocused. Returns the number of views changed.
        /// </summary>
        public static int SetPlayFocusedViewsToPlayUnfocused()
        {
            return SetBehaviorForMatchingViews(
                PlayModeView.EnterPlayModeBehavior.PlayFocused,
                PlayModeView.EnterPlayModeBehavior.PlayUnfocused);
        }

        /// <summary>
        /// Restores every PlayUnfocused view to PlayFocused. Returns the number of views changed.
        /// </summary>
        public static int SetPlayUnfocusedViewsToPlayFocused()
        {
            return SetBehaviorForMatchingViews(
                PlayModeView.EnterPlayModeBehavior.PlayUnfocused,
                PlayModeView.EnterPlayModeBehavior.PlayFocused);
        }

        private static int SetBehaviorForMatchingViews(
            PlayModeView.EnterPlayModeBehavior fromBehavior,
            PlayModeView.EnterPlayModeBehavior toBehavior)
        {
            List<PlayModeView> playModeViews = PlayModeView.GetAllPlayModeViewWindows();
            if (playModeViews == null)
            {
                return 0;
            }

            int changedViewCount = 0;
            for (int i = 0; i < playModeViews.Count; i++)
            {
                PlayModeView playModeView = playModeViews[i];
                // Why null check: Unity keeps destroyed windows in the static list until it prunes them.
                if (playModeView == null || playModeView.enterPlayModeBehavior != fromBehavior)
                {
                    continue;
                }

                // Safe transition: the setter's PlayMaximized cascade never runs for these two values.
                playModeView.enterPlayModeBehavior = toBehavior;
                changedViewCount++;
            }

            return changedViewCount;
        }
    }
}
