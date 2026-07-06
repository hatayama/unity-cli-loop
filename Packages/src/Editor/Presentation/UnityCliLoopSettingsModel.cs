using System;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Model layer for UnityCliLoopSettingsWindow in MVP architecture
    /// Handles state management and business logic using immutable state objects
    /// Related classes:
    /// - UnityCliLoopSettingsWindowState: State objects managed by this model
    /// - UnityCliLoopSettingsWindow: Presenter that uses this model
    /// - UnityCliLoopSettingsWindowUI: View layer for UI rendering
    /// - IUnityCliLoopEditorSettingsPort: Persistent settings storage boundary
    /// </summary>
    public class UnityCliLoopSettingsModel
    {
        public UIState UI { get; private set; }
        public RuntimeState Runtime { get; private set; }
        private readonly ToolSettingsUseCase _toolSettingsUseCase;
        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;

        internal UnityCliLoopSettingsModel(
            ToolSettingsUseCase toolSettingsUseCase,
            IUnityCliLoopEditorSettingsPort editorSettingsPort)
        {
            System.Diagnostics.Debug.Assert(toolSettingsUseCase != null, "toolSettingsUseCase must not be null");
            System.Diagnostics.Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");

            _toolSettingsUseCase = toolSettingsUseCase
                ?? throw new ArgumentNullException(nameof(toolSettingsUseCase));
            _editorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
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
            UnityCliLoopEditorSettingsData settings = _editorSettingsPort.GetSettings();
            
            UpdateUIState(ui => new UIState(
                mainScrollPosition: ui.MainScrollPosition,
                showUnityCliLoopSecuritySetting: settings.showUnityCliLoopSecuritySetting,
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
                mainScrollPosition: ui.MainScrollPosition,
                showUnityCliLoopSecuritySetting: ui.ShowUnityCliLoopSecuritySetting,
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
                lastServerRunning: runtime.LastServerRunning));
        }

        /// <summary>
        /// Exit post-compile mode
        /// </summary>
        public void DisablePostCompileMode()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: false,
                needsRepaint: runtime.NeedsRepaint,
                lastServerRunning: runtime.LastServerRunning));
        }

        /// <summary>
        /// Mark that UI repaint is needed
        /// </summary>
        public void RequestRepaint()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: runtime.IsPostCompileMode,
                needsRepaint: true,
                lastServerRunning: runtime.LastServerRunning));
        }

        /// <summary>
        /// Clear repaint request
        /// </summary>
        public void ClearRepaintRequest()
        {
            UpdateRuntimeState(runtime => new RuntimeState(
                isPostCompileMode: runtime.IsPostCompileMode,
                needsRepaint: false,
                lastServerRunning: runtime.LastServerRunning));
        }

        // UIState-specific update methods with persistence

        /// <summary>
        /// Update MainScrollPosition setting
        /// </summary>
        public void UpdateMainScrollPosition(Vector2 position)
        {
            UpdateUIState(ui => new UIState(
                mainScrollPosition: position,
                showUnityCliLoopSecuritySetting: ui.ShowUnityCliLoopSecuritySetting,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: ui.ShowConfiguration));
        }

        /// <summary>
        /// Update ShowUnityCliLoopSecuritySetting setting with persistence
        /// </summary>
        public void UpdateShowUnityCliLoopSecuritySetting(bool show)
        {
            UpdateUIState(ui => new UIState(
                mainScrollPosition: ui.MainScrollPosition,
                showUnityCliLoopSecuritySetting: show,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: ui.ShowConfiguration));
            _editorSettingsPort.SetShowUnityCliLoopSecuritySetting(show);
        }

        public void UpdateShowToolSettings(bool show)
        {
            UpdateUIState(ui => new UIState(
                mainScrollPosition: ui.MainScrollPosition,
                showUnityCliLoopSecuritySetting: ui.ShowUnityCliLoopSecuritySetting,
                showToolSettings: show,
                showConfiguration: ui.ShowConfiguration));
            _editorSettingsPort.SetShowToolSettings(show);
        }

        public void UpdateToolEnabled(string toolName, bool enabled)
        {
            _toolSettingsUseCase.SetToolEnabled(toolName, enabled);
        }

        public void UpdateShowConfiguration(bool show)
        {
            UpdateUIState(ui => new UIState(
                mainScrollPosition: ui.MainScrollPosition,
                showUnityCliLoopSecuritySetting: ui.ShowUnityCliLoopSecuritySetting,
                showToolSettings: ui.ShowToolSettings,
                showConfiguration: show));
        }

    }
}
