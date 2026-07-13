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
        private readonly Action<string, string, object> _logVibeInfo;
        private readonly Action<string, string, object> _logVibeWarning;

        internal ExternalAssetFocusReturnService(
            Func<bool> getAutoRefreshHeld,
            Action<bool> setAutoRefreshHeld,
            Func<bool> isEditorFocused,
            Action disallowAutoRefresh,
            Action allowAutoRefresh,
            Action resolveFocusReturnChanges,
            Action<string> logWarning = null,
            Action<string, string, object> logVibeInfo = null,
            Action<string, string, object> logVibeWarning = null)
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
            // Why inject: pure C# unit tests stay free of VibeLogger; production wires VibeLogger.
            _logVibeInfo = logVibeInfo ?? ((operation, message, context) => { });
            _logVibeWarning = logVibeWarning ?? ((operation, message, context) => { });
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
                if (HoldAutoRefreshIfNeeded())
                {
                    // Why only on actual repair: reconcile ticks every 0.5s; spam would drown the gate timeline.
                    _logVibeInfo(
                        "external_scene_reconcile_repair",
                        "Reconcile armed Auto Refresh hold while Editor is unfocused",
                        new { held = true, isFocused = false });
                }

                return;
            }

            if (!_getAutoRefreshHeld())
            {
                return;
            }

            HandleFocusChanged(true);
            _logVibeInfo(
                "external_scene_reconcile_repair",
                "Reconcile released Auto Refresh hold while Editor is focused",
                new { held = _getAutoRefreshHeld(), isFocused = true });
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

        /// <summary>
        /// Attempts to arm DisallowAutoRefresh. Returns true only when this call newly armed the hold.
        /// </summary>
        internal bool HoldAutoRefreshIfNeeded()
        {
            if (_getAutoRefreshHeld())
            {
                return false;
            }

            if (!TryDisallowAutoRefresh())
            {
                return false;
            }

            // Why only after success: setting SessionState on failure desyncs the Unity counter (§10).
            _setAutoRefreshHeld(true);
            _logVibeInfo(
                "external_scene_hold_armed",
                "Auto Refresh hold armed",
                new { held = true, isFocused = _isEditorFocused() });
            return true;
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
            _logVibeInfo(
                "external_scene_hold_released",
                "Auto Refresh hold released",
                new { held = false, isFocused = _isEditorFocused() });
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
                _logVibeWarning(
                    "external_scene_hold_failed",
                    "DisallowAutoRefresh failed",
                    new
                    {
                        exceptionType = exception.GetType().FullName,
                        exceptionMessage = exception.Message,
                        held = _getAutoRefreshHeld(),
                        isFocused = _isEditorFocused()
                    });
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
                _logVibeWarning(
                    "external_scene_release_failed",
                    "AllowAutoRefresh failed",
                    new
                    {
                        exceptionType = exception.GetType().FullName,
                        exceptionMessage = exception.Message,
                        held = _getAutoRefreshHeld(),
                        isFocused = _isEditorFocused()
                    });
                return false;
            }
        }
    }
}
