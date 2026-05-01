using UnityEngine;
using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// State management objects for McpEditorWindow
    /// Implements State Object pattern as Model layer in MVP architecture
    /// Uses get-only properties for with expression compatibility
    /// Related classes:
    /// - McpEditorWindow: Presenter layer that uses these state objects
    /// - McpEditorWindowView: View layer for UI rendering
    /// - McpEditorModel: Model layer service for managing state transitions
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
        public bool ShowConnectedTools { get; }
        public Vector2 MainScrollPosition { get; }
        public bool ShowSecuritySettings { get; }
        public bool ShowToolSettings { get; }
        public bool ShowConfiguration { get; }

        public UIState(
            bool showConnectedTools = true,
            Vector2 mainScrollPosition = default,
            bool showSecuritySettings = true,
            bool showToolSettings = true,
            bool showConfiguration = true)
        {
            ShowConnectedTools = showConnectedTools;
            MainScrollPosition = mainScrollPosition;
            ShowSecuritySettings = showSecuritySettings;
            ShowToolSettings = showToolSettings;
            ShowConfiguration = showConfiguration;
        }
    }

    /// <summary>
    /// Runtime state data for McpEditorWindow
    /// Tracks dynamic state during editor window operation
    /// </summary>
    public record RuntimeState
    {
        public bool NeedsRepaint { get; }
        public bool IsPostCompileMode { get; }
        public bool LastServerRunning { get; }
        public int LastConnectedClientsCount { get; }
        public string LastClientsInfoHash { get; }

        public RuntimeState(
            bool needsRepaint = false,
            bool isPostCompileMode = false,
            bool lastServerRunning = false,
            int lastConnectedClientsCount = 0,
            string lastClientsInfoHash = "")
        {
            NeedsRepaint = needsRepaint;
            IsPostCompileMode = isPostCompileMode;
            LastServerRunning = lastServerRunning;
            LastConnectedClientsCount = lastConnectedClientsCount;
            LastClientsInfoHash = lastClientsInfoHash;
        }
    }

} 
