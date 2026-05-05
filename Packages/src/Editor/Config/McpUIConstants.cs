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

        // Communication log settings
        public const int MAX_COMMUNICATION_LOG_ENTRIES = 20;

        // Tool Settings
        public const string TOOL_SETTINGS_MENU_PATH = "Window > Unity CLI Loop > Settings";
        public const string CLI_COMMAND_REFERENCE_URL = "https://github.com/hatayama/unity-cli-loop#direct-cli-usage-advanced";
        public const string PROJECT_REPOSITORY_URL = "https://github.com/hatayama/unity-cli-loop";
    }
}
