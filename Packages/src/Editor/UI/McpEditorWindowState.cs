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

    public enum ConnectionMode
    {
        CLI = 0,
        MCP = 1
    }

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
        public int CustomPort { get; }
        public bool ShowConnectedTools { get; }
        public McpEditorType SelectedEditorType { get; }
        public Vector2 MainScrollPosition { get; }
        public bool ShowSecuritySettings { get; }
        public bool ShowToolSettings { get; }
        public bool AddRepositoryRoot { get; }
        public bool SupportsRepositoryRootToggle { get; }
        public bool ShowRepositoryRootToggle { get; }
        public ConnectionMode ConnectionMode { get; }
        public bool ShowConfiguration { get; }

        public UIState(
            int customPort = McpServerConfig.DEFAULT_PORT,
            bool showConnectedTools = true,
            McpEditorType selectedEditorType = McpEditorType.Cursor,
            Vector2 mainScrollPosition = default,
            bool showSecuritySettings = true,
            bool showToolSettings = true,
            bool addRepositoryRoot = false,
            bool supportsRepositoryRootToggle = false,
            bool showRepositoryRootToggle = false,
            ConnectionMode connectionMode = ConnectionMode.CLI,
            bool showConfiguration = true)
        {
            CustomPort = customPort;
            ShowConnectedTools = showConnectedTools;
            SelectedEditorType = selectedEditorType;
            MainScrollPosition = mainScrollPosition;
            ShowSecuritySettings = showSecuritySettings;
            ShowToolSettings = showToolSettings;
            AddRepositoryRoot = addRepositoryRoot;
            SupportsRepositoryRootToggle = supportsRepositoryRootToggle;
            ShowRepositoryRootToggle = showRepositoryRootToggle;
            ConnectionMode = connectionMode;
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
        public int LastServerPort { get; }
        public int LastConnectedClientsCount { get; }
        public string LastClientsInfoHash { get; }

        public RuntimeState(
            bool needsRepaint = false,
            bool isPostCompileMode = false,
            bool lastServerRunning = false,
            int lastServerPort = 0,
            int lastConnectedClientsCount = 0,
            string lastClientsInfoHash = "")
        {
            NeedsRepaint = needsRepaint;
            IsPostCompileMode = isPostCompileMode;
            LastServerRunning = lastServerRunning;
            LastServerPort = lastServerPort;
            LastConnectedClientsCount = lastConnectedClientsCount;
            LastClientsInfoHash = lastClientsInfoHash;
        }
    }

} 
