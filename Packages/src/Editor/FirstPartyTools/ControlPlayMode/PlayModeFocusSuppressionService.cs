using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Suppresses Unity's Play Mode window raise while the Editor is unfocused by forcing
    /// PlayFocused views to PlayUnfocused, and restores them when the Editor regains focus.
    /// Why not rely on focusChanged alone: background launch never fires focus-lost, and domain
    /// reloads or editor restarts can drop the event, so suppression must also be armed and
    /// released from startup and periodic reconcile.
    /// </summary>
    internal sealed class PlayModeFocusSuppressionService
    {
        private readonly Func<bool> _isEditorFocused;
        private readonly Func<int> _suppressPlayFocusedViews;
        private readonly Func<int> _restorePlayUnfocusedViews;
        private readonly Func<bool> _getSuppressedFlag;
        private readonly Action<bool> _setSuppressedFlag;
        private readonly Action<string, string, object> _logVibeInfo;

        internal PlayModeFocusSuppressionService(
            Func<bool> isEditorFocused,
            Func<int> suppressPlayFocusedViews,
            Func<int> restorePlayUnfocusedViews,
            Func<bool> getSuppressedFlag,
            Action<bool> setSuppressedFlag,
            Action<string, string, object> logVibeInfo = null)
        {
            Debug.Assert(isEditorFocused != null, "isEditorFocused must not be null");
            Debug.Assert(suppressPlayFocusedViews != null, "suppressPlayFocusedViews must not be null");
            Debug.Assert(restorePlayUnfocusedViews != null, "restorePlayUnfocusedViews must not be null");
            Debug.Assert(getSuppressedFlag != null, "getSuppressedFlag must not be null");
            Debug.Assert(setSuppressedFlag != null, "setSuppressedFlag must not be null");

            _isEditorFocused = isEditorFocused ?? throw new ArgumentNullException(nameof(isEditorFocused));
            _suppressPlayFocusedViews =
                suppressPlayFocusedViews ?? throw new ArgumentNullException(nameof(suppressPlayFocusedViews));
            _restorePlayUnfocusedViews =
                restorePlayUnfocusedViews ?? throw new ArgumentNullException(nameof(restorePlayUnfocusedViews));
            _getSuppressedFlag = getSuppressedFlag ?? throw new ArgumentNullException(nameof(getSuppressedFlag));
            _setSuppressedFlag = setSuppressedFlag ?? throw new ArgumentNullException(nameof(setSuppressedFlag));
            // Why inject: pure C# unit tests stay free of VibeLogger; production wires VibeLogger.
            _logVibeInfo = logVibeInfo ?? ((operation, message, context) => { });
        }

        /// <summary>
        /// Applies suppress or restore for a focus transition. Idempotent for repeated events.
        /// </summary>
        internal void HandleFocusChanged(bool isFocused)
        {
            if (isFocused)
            {
                RestoreIfSuppressed();
                return;
            }

            SuppressWhileUnfocused();
        }

        /// <summary>
        /// Aligns suppression with the current focus without depending on focusChanged delivery.
        /// Idempotent and cheap, so it also re-suppresses Game views opened while still unfocused.
        /// </summary>
        internal void Reconcile()
        {
            HandleFocusChanged(_isEditorFocused());
        }

        private void SuppressWhileUnfocused()
        {
            int changedViews = _suppressPlayFocusedViews();
            if (changedViews <= 0)
            {
                // Why keep the flag as-is: an already-armed suppression must survive no-op calls.
                return;
            }

            _setSuppressedFlag(true);
            // Why only on actual change: reconcile ticks every 0.5s; spam would drown the log timeline.
            _logVibeInfo(
                "play_focus_suppress_armed",
                "Forced PlayFocused views to PlayUnfocused while the Editor is unfocused",
                new { changedViews, isFocused = _isEditorFocused() });
        }

        private void RestoreIfSuppressed()
        {
            if (!_getSuppressedFlag())
            {
                // Why no restore call: views the user set to PlayUnfocused must stay untouched.
                return;
            }

            int changedViews = _restorePlayUnfocusedViews();
            _setSuppressedFlag(false);
            // Why log even with zero changed views: the flag transition itself must stay observable.
            _logVibeInfo(
                "play_focus_suppress_released",
                "Restored PlayUnfocused views to PlayFocused after the Editor regained focus",
                new { changedViews, isFocused = _isEditorFocused() });
        }
    }
}
