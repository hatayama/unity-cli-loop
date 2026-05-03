using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Unity CLI Loop Editor UI constants
    /// Centralized management of UI-related constants
    /// </summary>
    public static class McpUIConstants
    {
        // Communication log area
        public const float DEFAULT_COMMUNICATION_LOG_HEIGHT = 300f;

        // UI spacing and dimensions
        public const float BUTTON_HEIGHT_LARGE = 30f;
        
        // UI colors
        private const float SECTION_BACKGROUND_COLOR_ONE = 0.18f;
        public static readonly Color SECTION_BACKGROUND_COLOR = new(SECTION_BACKGROUND_COLOR_ONE, SECTION_BACKGROUND_COLOR_ONE, SECTION_BACKGROUND_COLOR_ONE, 1f);

        private const float CLIENT_ITEM_BACKGROUND_COLOR_ONE = 0.3f;
        public static readonly Color CLIENT_ITEM_BACKGROUND_COLOR = new(CLIENT_ITEM_BACKGROUND_COLOR_ONE, CLIENT_ITEM_BACKGROUND_COLOR_ONE, CLIENT_ITEM_BACKGROUND_COLOR_ONE, 0.5f);

        // Communication log settings
        public const int MAX_COMMUNICATION_LOG_ENTRIES = 20;
        
        // Connected clients display
        public const float CLIENT_ITEM_SPACING = 3f;
        public const string CONNECTED_TOOLS_FOLDOUT_TEXT = "Connected LLM Tools";
        public const string CLIENT_ICON = "● ";

        // Tool Settings
        public const string TOOL_SETTINGS_MENU_PATH = "Window > Unity CLI Loop > Settings";
        public const string CLI_COMMAND_REFERENCE_URL = "https://github.com/hatayama/unity-cli-loop#direct-cli-usage-advanced";
        public const string PROJECT_REPOSITORY_URL = "https://github.com/hatayama/unity-cli-loop";
    }
}
