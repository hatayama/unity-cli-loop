using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Wires PlayModeFocusSuppressionService to editor focus events and a throttled update reconcile.
    /// </summary>
    internal static class PlayModeFocusSuppressionStartup
    {
        // Why throttle: reconcile must not walk the play-mode view list every frame when already aligned.
        private const double ReconcileIntervalSeconds = 0.5d;
        private static readonly PlayModeFocusSuppressionService Service =
            new PlayModeFocusSuppressionService(
                () => EditorApplication.isFocused,
                PlayModeViewFocusBridge.SetPlayFocusedViewsToPlayUnfocused,
                PlayModeViewFocusBridge.SetPlayUnfocusedViewsToPlayFocused,
                IsSuppressed,
                SetSuppressed,
                logVibeInfo: (operation, message, context) =>
                {
                    VibeLogger.LogInfo(operation, message, context, includeStackTrace: false);
                });
        private static bool _initialized;
        private static double _nextReconcileTime;

        internal static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            EditorApplication.focusChanged -= HandleFocusChanged;
            EditorApplication.focusChanged += HandleFocusChanged;
            EditorApplication.update -= ReconcileOnUpdate;
            EditorApplication.update += ReconcileOnUpdate;
            // Why immediate reconcile: background launch never fires focusChanged(false), so views must
            // be suppressed right away; a stale flag from a crash while unfocused is released here too.
            Service.Reconcile();
        }

        private static void HandleFocusChanged(bool isFocused)
        {
            Service.HandleFocusChanged(isFocused);
        }

        private static void ReconcileOnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextReconcileTime)
            {
                return;
            }

            _nextReconcileTime = now + ReconcileIntervalSeconds;
            Service.Reconcile();
        }

        private static bool IsSuppressed()
        {
            return EditorUserSettings.GetConfigValue(PlayModeFocusSuppressionConstants.SuppressedConfigKey) ==
                   PlayModeFocusSuppressionConstants.SuppressedConfigValue;
        }

        private static void SetSuppressed(bool isSuppressed)
        {
            // Why null on clear: EditorUserSettings removes the entry, keeping the project settings clean.
            EditorUserSettings.SetConfigValue(
                PlayModeFocusSuppressionConstants.SuppressedConfigKey,
                isSuppressed ? PlayModeFocusSuppressionConstants.SuppressedConfigValue : null);
        }
    }
}
