using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// State management objects for UnityCliLoopSettingsWindow
    /// Implements State Object pattern as Model layer in MVP architecture
    /// Uses get-only properties for with expression compatibility
    /// Related classes:
    /// - UnityCliLoopSettingsWindow: Presenter layer that uses these state objects
    /// - UnityCliLoopSettingsWindowUI: View layer for UI rendering
    /// - UnityCliLoopSettingsModel: Model layer service for managing state transitions
    /// </summary>

    public enum SkillsTarget
    {
        Claude = 0,
        Codex = 4,
        Cursor = 2,
        Gemini = 3,
        [InspectorName("Other (.agents)")]
        Agents = 1
    }

    public record UIState
    {
        public Vector2 MainScrollPosition { get; }
        public bool ShowUnityCliLoopSecuritySetting { get; }
        public bool ShowToolSettings { get; }
        public bool ShowConfiguration { get; }

        public UIState(
            Vector2 mainScrollPosition = default,
            bool showUnityCliLoopSecuritySetting = true,
            bool showToolSettings = true,
            bool showConfiguration = true)
        {
            MainScrollPosition = mainScrollPosition;
            ShowUnityCliLoopSecuritySetting = showUnityCliLoopSecuritySetting;
            ShowToolSettings = showToolSettings;
            ShowConfiguration = showConfiguration;
        }
    }

    /// <summary>
    /// Runtime state data for UnityCliLoopSettingsWindow
    /// Tracks dynamic state during editor window operation
    /// </summary>
    public record RuntimeState
    {
        public bool NeedsRepaint { get; }
        public bool IsPostCompileMode { get; }
        public bool LastServerRunning { get; }

        public RuntimeState(
            bool needsRepaint = false,
            bool isPostCompileMode = false,
            bool lastServerRunning = false)
        {
            NeedsRepaint = needsRepaint;
            IsPostCompileMode = isPostCompileMode;
            LastServerRunning = lastServerRunning;
        }
    }

} 
