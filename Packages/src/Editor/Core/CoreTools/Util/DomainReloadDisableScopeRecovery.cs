using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Restores Enter Play Mode settings when DomainReloadDisableScope was interrupted.
    /// </summary>
    internal static class DomainReloadDisableScopeRecovery
    {
        [InitializeOnLoadMethod]
        private static void RestorePendingSettingsOnEditorLoad()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            RestoreIfPending();
        }

        /// <summary>
        /// Saves the current Enter Play Mode settings before DomainReloadDisableScope changes them.
        /// </summary>
        internal static void SaveCurrentSettingsIfNeeded()
        {
            McpEditorSettingsData settings = McpEditorSettings.GetSettings();
            if (settings.domainReloadDisableScopeRestorePending)
            {
                return;
            }

            McpEditorSettings.UpdateSettings(current => current with
            {
                domainReloadDisableScopeRestorePending = true,
                domainReloadDisableScopeOriginalOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                domainReloadDisableScopeOriginalOptions = (int)EditorSettings.enterPlayModeOptions
            });
        }

        /// <summary>
        /// Restores Enter Play Mode settings saved before DomainReloadDisableScope changed them.
        /// </summary>
        internal static void RestoreIfPending()
        {
            McpEditorSettingsData settings = McpEditorSettings.GetSettings();
            if (!settings.domainReloadDisableScopeRestorePending)
            {
                return;
            }

            EditorSettings.enterPlayModeOptionsEnabled = settings.domainReloadDisableScopeOriginalOptionsEnabled;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)settings.domainReloadDisableScopeOriginalOptions;
            ClearPendingRestore();
        }

        /// <summary>
        /// Clears any saved Enter Play Mode settings pending restore.
        /// </summary>
        internal static void ClearPendingRestore()
        {
            McpEditorSettings.UpdateSettings(settings => settings with
            {
                domainReloadDisableScopeRestorePending = false,
                domainReloadDisableScopeOriginalOptionsEnabled = false,
                domainReloadDisableScopeOriginalOptions = (int)EnterPlayModeOptions.None
            });
        }
    }
}
