using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Auto Refresh suspension while Unity is unfocused.
    /// Why not rely on focusChanged alone: background launch never fires focus-lost, so
    /// DisallowAutoRefresh must also be armed from Initialize and periodic reconcile.
    /// </summary>
    internal sealed class ExternalAssetFocusReturnService
    {
        private readonly Func<bool> _getAutoRefreshHeld;
        private readonly Action<bool> _setAutoRefreshHeld;
        private readonly Func<bool> _isEditorFocused;
        private readonly Action _disallowAutoRefresh;
        private readonly Action _allowAutoRefresh;
        private readonly Action _resolveFocusReturnChanges;
        private readonly Action<string> _logWarning;

        internal ExternalAssetFocusReturnService(
            Func<bool> getAutoRefreshHeld,
            Action<bool> setAutoRefreshHeld,
            Func<bool> isEditorFocused,
            Action disallowAutoRefresh,
            Action allowAutoRefresh,
            Action resolveFocusReturnChanges,
            Action<string> logWarning = null)
        {
            Debug.Assert(getAutoRefreshHeld != null, "getAutoRefreshHeld must not be null");
            Debug.Assert(setAutoRefreshHeld != null, "setAutoRefreshHeld must not be null");
            Debug.Assert(isEditorFocused != null, "isEditorFocused must not be null");
            Debug.Assert(disallowAutoRefresh != null, "disallowAutoRefresh must not be null");
            Debug.Assert(allowAutoRefresh != null, "allowAutoRefresh must not be null");
            Debug.Assert(resolveFocusReturnChanges != null, "resolveFocusReturnChanges must not be null");

            _getAutoRefreshHeld = getAutoRefreshHeld ?? throw new ArgumentNullException(nameof(getAutoRefreshHeld));
            _setAutoRefreshHeld = setAutoRefreshHeld ?? throw new ArgumentNullException(nameof(setAutoRefreshHeld));
            _isEditorFocused = isEditorFocused ?? throw new ArgumentNullException(nameof(isEditorFocused));
            _disallowAutoRefresh = disallowAutoRefresh ?? throw new ArgumentNullException(nameof(disallowAutoRefresh));
            _allowAutoRefresh = allowAutoRefresh ?? throw new ArgumentNullException(nameof(allowAutoRefresh));
            _resolveFocusReturnChanges =
                resolveFocusReturnChanges ?? throw new ArgumentNullException(nameof(resolveFocusReturnChanges));
            _logWarning = logWarning ?? (message => Debug.LogWarning(message));
        }

        internal bool RestoreAutoRefreshIfHeld()
        {
            if (!_getAutoRefreshHeld())
            {
                return false;
            }

            if (!_isEditorFocused())
            {
                return false;
            }

            HandleFocusChanged(true);
            return true;
        }

        /// <summary>
        /// Arms DisallowAutoRefresh when the Editor starts unfocused (no focus-lost event yet).
        /// </summary>
        internal void HoldIfCurrentlyUnfocused()
        {
            if (_isEditorFocused())
            {
                return;
            }

            HoldAutoRefreshIfNeeded();
        }

        /// <summary>
        /// Aligns held flag with focus without depending on focusChanged delivery.
        /// Idempotent: only calls Disallow/Allow when state must change.
        /// Why not delayCall retry chains: kCodeReload failures stay unheld and this reconcile retries later.
        /// </summary>
        internal void ReconcileAutoRefreshHoldWithFocus()
        {
            if (!_isEditorFocused())
            {
                HoldAutoRefreshIfNeeded();
                return;
            }

            if (!_getAutoRefreshHeld())
            {
                return;
            }

            HandleFocusChanged(true);
        }

        internal void HandleFocusChanged(bool isFocused)
        {
            if (!isFocused)
            {
                HoldAutoRefreshIfNeeded();
                return;
            }

            try
            {
                _resolveFocusReturnChanges();
            }
            finally
            {
                ReleaseAutoRefreshIfHeld();
            }
        }

        internal void HoldAutoRefreshIfNeeded()
        {
            if (_getAutoRefreshHeld())
            {
                return;
            }

            if (!TryDisallowAutoRefresh())
            {
                return;
            }

            // Why only after success: setting SessionState on failure desyncs the Unity counter (§10).
            _setAutoRefreshHeld(true);
        }

        private void ReleaseAutoRefreshIfHeld()
        {
            if (!_getAutoRefreshHeld())
            {
                return;
            }

            if (!TryAllowAutoRefresh())
            {
                return;
            }

            _setAutoRefreshHeld(false);
        }

        private bool TryDisallowAutoRefresh()
        {
            // Why try-catch (hatayama-approved, Disallow/Allow boundary only): Unity throws during kCodeReload.
            try
            {
                _disallowAutoRefresh();
                return true;
            }
            catch (Exception exception)
            {
                _logWarning(
                    "Unity CLI Loop could not DisallowAutoRefresh (often during domain reload). " +
                    "Will retry via focus reconcile. " + exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }

        private bool TryAllowAutoRefresh()
        {
            try
            {
                _allowAutoRefresh();
                return true;
            }
            catch (Exception exception)
            {
                _logWarning(
                    "Unity CLI Loop could not AllowAutoRefresh (often during domain reload). " +
                    "Will retry via focus reconcile. " + exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }
    }
}
