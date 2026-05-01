using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Model layer for McpEditorWindow in MVP architecture
    /// Handles state management and business logic using immutable state objects
    /// Related classes:
    /// - McpEditorWindowState: State objects managed by this model
    /// - McpEditorWindow: Presenter that uses this model
    /// - McpEditorWindowView: View layer for UI rendering
    /// - McpEditorSettings: Persistent settings storage
    /// </summary>
    public class McpEditorModel
    {
        public UIState UI { get; private set; }
        public RuntimeState Runtime { get; private set; }

        public McpEditorModel()
        {
            UI = new UIState();
            Runtime = new RuntimeState();
        }

        /// <summary>
        /// Update UI state with new values
        /// </summary>
        /// <param name="updater">Function to update UI state</param>
        public void UpdateUIState(Func<UIState, UIState> updater)
        {
            UI = updater(UI);
        }

        /// <summary>
        /// Update runtime state with new values
        /// </summary>
        /// <param name="updater">Function to update runtime state</param>
        public void UpdateRuntimeState(Func<RuntimeState, RuntimeState> updater)
        {
            Runtime = updater(Runtime);
        }

        /// <summary>
        /// Load state from persistent settings
        /// </summary>
        public void LoadFromSettings()
        {
            McpEditorSettingsData settings = McpEditorSettings.GetSettings();
            
            UpdateUIState(ui => new UIState(
                showConnectedTools: ui.ShowConnectedTools,
                mainScrollPosition: ui.MainScrollPosition,
                showSecuritySettings: settings.showSecuritySettings,
                showToolSettings: settings.showToolSettings,
                showConfiguration: ui.ShowConfiguration));

        }

        /// <summary>
        /// Save current UI state to persistent settings
        /// </summary>
        public void SaveToSettings()
        {
        }

        /// <summary>
        /// Load state from persistent settings (formerly from SessionState)
        /// </summary>
        public void LoadFromSessionState()
        {
            UpdateUIState(ui => new UIState(
                showConnectedTools: ui.ShowConnectedTools,
                mainScrollPosition: ui.MainScrollPosition,
                showSecuritySettings: ui.ShowSecuritySettings,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: ui.ShowConfiguration));
        }

        /// <summary>
        /// Save current state to persistent settings (formerly to SessionState)
        /// </summary>
        public void SaveToSessionState()
        {
        }

        /// <summary>
        /// Initialize post-compile mode
        /// </summary>
        public void EnablePostCompileMode()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: true,
                needsRepaint: true,
                lastServerRunning: runtime.LastServerRunning,
                lastConnectedClientsCount: runtime.LastConnectedClientsCount,
                lastClientsInfoHash: runtime.LastClientsInfoHash));
        }

        /// <summary>
        /// Exit post-compile mode
        /// </summary>
        public void DisablePostCompileMode()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: false,
                needsRepaint: runtime.NeedsRepaint,
                lastServerRunning: runtime.LastServerRunning,
                lastConnectedClientsCount: runtime.LastConnectedClientsCount,
                lastClientsInfoHash: runtime.LastClientsInfoHash));
        }

        /// <summary>
        /// Mark that UI repaint is needed
        /// </summary>
        public void RequestRepaint()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: runtime.IsPostCompileMode,
                needsRepaint: true,
                lastServerRunning: runtime.LastServerRunning,
                lastConnectedClientsCount: runtime.LastConnectedClientsCount,
                lastClientsInfoHash: runtime.LastClientsInfoHash));
        }

        /// <summary>
        /// Clear repaint request
        /// </summary>
        public void ClearRepaintRequest()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: runtime.IsPostCompileMode,
                needsRepaint: false,
                lastServerRunning: runtime.LastServerRunning,
                lastConnectedClientsCount: runtime.LastConnectedClientsCount,
                lastClientsInfoHash: runtime.LastClientsInfoHash));
        }

        /// <summary>
        /// Update server state tracking for change detection
        /// </summary>
        public void UpdateServerStateTracking(bool isRunning, int clientCount, string clientsHash)
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: runtime.IsPostCompileMode,
                needsRepaint: runtime.NeedsRepaint,
                lastServerRunning: isRunning,
                lastConnectedClientsCount: clientCount,
                lastClientsInfoHash: clientsHash));
        }

        // UIState-specific update methods with persistence

        /// <summary>
        /// Update ShowConnectedTools setting
        /// </summary>
        public void UpdateShowConnectedTools(bool show)
        {
            UpdateUIState(ui => new UIState(
                showConnectedTools: show,
                mainScrollPosition: ui.MainScrollPosition,
                showSecuritySettings: ui.ShowSecuritySettings,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: ui.ShowConfiguration));
        }

        /// <summary>
        /// Update MainScrollPosition setting
        /// </summary>
        public void UpdateMainScrollPosition(Vector2 position)
        {
            UpdateUIState(ui => new UIState(
                showConnectedTools: ui.ShowConnectedTools,
                mainScrollPosition: position,
                showSecuritySettings: ui.ShowSecuritySettings,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: ui.ShowConfiguration));
        }

        /// <summary>
        /// Update ShowSecuritySettings setting with persistence
        /// </summary>
        public void UpdateShowSecuritySettings(bool show)
        {
            UpdateUIState(ui => new UIState(
                showConnectedTools: ui.ShowConnectedTools,
                mainScrollPosition: ui.MainScrollPosition,
                showSecuritySettings: show,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: ui.ShowConfiguration));
            McpEditorSettings.SetShowSecuritySettings(show);
        }

        public void UpdateShowToolSettings(bool show)
        {
            UpdateUIState(ui => new UIState(
                showConnectedTools: ui.ShowConnectedTools,
                mainScrollPosition: ui.MainScrollPosition,
                showSecuritySettings: ui.ShowSecuritySettings,
                showToolSettings: show,
                showConfiguration: ui.ShowConfiguration));
            McpEditorSettings.SetShowToolSettings(show);
        }

        public void UpdateToolEnabled(string toolName, bool enabled)
        {
            ToolSettings.SetToolEnabled(toolName, enabled);
        }

        /// <summary>
        /// Update AllowThirdPartyTools setting with persistence
        /// </summary>
        public void UpdateAllowThirdPartyTools(bool allow)
        {
            ULoopSettings.SetAllowThirdPartyTools(allow);
        }

        public void UpdateShowConfiguration(bool show)
        {
            UpdateUIState(ui => new UIState(
                showConnectedTools: ui.ShowConnectedTools,
                mainScrollPosition: ui.MainScrollPosition,
                showSecuritySettings: ui.ShowSecuritySettings,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: show));
        }

    }
}
