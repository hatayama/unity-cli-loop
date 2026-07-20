namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Central constants for shared package paths, command names, and protocol-independent limits.
    /// </summary>
    public static class UnityCliLoopConstants
    {
        private static UnityEditor.PackageManager.PackageInfo _cachedPackageInfo;

        public static UnityEditor.PackageManager.PackageInfo PackageInfo
        {
            get
            {
                if (_cachedPackageInfo == null)
                {
                    _cachedPackageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                        typeof(UnityCliLoopConstants).Assembly);

                    if (_cachedPackageInfo == null)
                    {
                        throw new System.InvalidOperationException(
                            "Failed to resolve PackageInfo for UnityCliLoop. " +
                            "Ensure the package is properly installed via Package Manager.");
                    }
                }
                return _cachedPackageInfo;
            }
        }

        public static string PackageAssetPath => PackageInfo.assetPath;

        public static string PackageResolvedPath => PackageInfo.resolvedPath;

        public const string PROJECT_NAME = "UnityCliLoop";
        public const string PACKAGE_NAME = "io.github.hatayama.uloopmcp";
        
        // Editor settings
        public const string SETTINGS_FILE_NAME = "UnityCliLoopSettings.json";
        public const string LEGACY_SETTINGS_FILE_NAME = "UnityMcpSettings.json";
        public const string USER_SETTINGS_FOLDER = "UserSettings";
        
        // Scripting define symbols
        public const string SCRIPTING_DEFINE_ULOOP_DEBUG = "ULOOP_DEBUG";
        public const string SCRIPTING_DEFINE_HAS_TEST_FRAMEWORK = "ULOOP_HAS_TEST_FRAMEWORK";
        
        // Environment variable keys for development mode
        public const string ENV_KEY_ULOOP_DEBUG = "ULOOP_DEBUG";
        // Reconnection settings
        public const int RECONNECTION_TIMEOUT_SECONDS = 10;
        
        // Package path constants
        public const string TEMP_DIR = "Temp";
        public const string UNITYCLILOOP_DIR = "UnityCliLoop";
        public const string JSON_FILE_EXTENSION = ".json";
        
        // .uloop directory
        public const string ULOOP_DIR = ".uloop";
        public const string ULOOP_TOOL_SETTINGS_FILE_NAME = "settings.tools.json";
        public const string ULOOP_PROJECT_RUNNER_PIN_FILE_NAME = "project-runner-pin.json";

        // Command name constants
        public const string TOOL_NAME_COMPILE = "compile";
        public const string TOOL_NAME_CONTROL_PLAY_MODE = "control-play-mode";
        public const string TOOL_NAME_ENABLE_PAUSE_POINT = "enable-pause-point";
        public const string TOOL_NAME_CLEAR_PAUSE_POINT = "clear-pause-point";
        public const string TOOL_NAME_EXECUTE_DYNAMIC_CODE = "execute-dynamic-code";
        public const string TOOL_NAME_GET_HIERARCHY = "get-hierarchy";
        public const string TOOL_NAME_GET_LOGS = "get-logs";
        public const string TOOL_NAME_RUN_TESTS = "run-tests";
        public const string TOOL_NAME_ENABLE_WATCH = "enable-watch";
        public const string TOOL_NAME_CLEAR_WATCH = "clear-watch";
        public const string TOOL_NAME_GET_WATCH_VALUES = "get-watch-values";
        public const string COMMAND_NAME_GET_TOOL_DETAILS = "get-tool-details";
        public const string COMMAND_NAME_GET_VERSION = "get-version";
        public const string COMMAND_NAME_GET_COMPILE_STATUS = "get-compile-status";
        public const string COMMAND_NAME_AWAIT_PAUSE_POINT = "await-pause-point";
        public const string SETTINGS_TOOL_NAME_PAUSE_POINT = "pause-point";
        public const string COMMAND_NAME_PAUSE_POINT_STATUS = "pause-point-status";
        public const string COMMAND_NAME_GET_PAUSE_POINT_STATUS = "get-pause-point-status";
        public const string COMMAND_NAME_CLEAR_PAUSE_POINT_STATUS = "clear-pause-point-status";
        public const string COMMAND_NAME_EXTEND_PAUSE_POINT_STATUS = "extend-pause-point-status";

        // Optional package names
        public const string PACKAGE_NAME_TEST_FRAMEWORK = "com.unity.test-framework";

        // File output directories
        public const string OUTPUT_ROOT_DIR = ".uloop/outputs";
        public const string TEST_RESULTS_DIR = "TestResults";
        public const string HIERARCHY_RESULTS_DIR = "HierarchyResults";
        public const string FIND_GAMEOBJECTS_RESULTS_DIR = "FindGameObjectsResults";
        public const string SCREENSHOTS_DIR = "Screenshots";
        public const string VIBE_LOGS_DIR = "VibeLogs";

        // Raycast tool constants
        public const float RAYCAST_DEFAULT_MAX_DISTANCE = 1000f;
        public const string COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW = "top-left-game-view";
        public const string COORDINATE_SYSTEM_BOTTOM_LEFT_GAME_VIEW = "bottom-left-game-view";
        public const string COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY =
            "unity_x = input_x; unity_y = gameViewHeight - input_y";

        // Screenshot tool coordinate constants
        public const string COORDINATE_SYSTEM_TOP_LEFT_WINDOW = "top-left-window";
        public const string SCREENSHOT_RENDERING_TO_INPUT_FORMULA =
            "simulate_mouse_x = image_x / resolutionScale; simulate_mouse_y = image_y / resolutionScale + imageToInputOffsetY";
        public const string SCREENSHOT_WINDOW_TO_INPUT_FORMULA_UNAVAILABLE =
            "unavailable: window screenshots include Unity Editor chrome; use capture-mode rendering for mouse input coordinates";
        public const string SCREENSHOT_DEFAULT_WINDOW_NAME = "Game";
        public const string SCREENSHOT_SIMULATOR_WINDOW_NAME = "Simulator";

        public const int CORRELATION_ID_LENGTH = 8;
        public const string GUID_FORMAT_NO_HYPHENS = "N";
        
        public const string ERROR_MESSAGE_DUPLICATE_ASMDEF = "Duplicate asmdef assembly name detected. Unity may not start compilation until duplicates are removed.";
        public const string ERROR_MESSAGE_ASSEMBLY_DEFINITION_IMPORT_ERROR = "Assembly Definition or Assembly Reference import error detected. Unity may not start compilation until asmdef or asmref errors are removed.";
        public const string ERROR_MESSAGE_EXECUTION_IN_PROGRESS = "Another execution is already in progress";
        public const string ERROR_MESSAGE_EXECUTION_CANCELLED = "Execution was cancelled or timed out";
        public const string ERROR_MESSAGE_DYNAMIC_CODE_RUNTIME_RESTARTING =
            "Dynamic-code runtime was disposed during a server reset or domain reload; retry the same command shortly.";
        public const string ERROR_MESSAGE_NO_COMPILED_ASSEMBLY = "No compiled assembly provided";
        public const string ERROR_MESSAGE_NO_EXECUTE_METHOD = "No Execute method found in compiled assembly";
        public const string ERROR_MESSAGE_FAILED_TO_CREATE_INSTANCE = "Failed to create instance of target type";
        public const string ERROR_MESSAGE_UNSUPPORTED_SIGNATURE = "Execute method signature not supported";

        /// <summary>
        /// Recovery steps when execute-dynamic-code hits a disposed runtime during reset/reload.
        /// Why this tone: matches compile-wait guidance — retry recovers the result; launch -r is not first-line.
        /// </summary>
        public static readonly string[] DYNAMIC_CODE_RUNTIME_RESTARTING_NEXT_ACTIONS =
        {
            "Retry the same `uloop execute-dynamic-code` shortly — the bridge is restarting after reset or domain reload.",
            "Unity is still running; `uloop launch -r` is not needed for this error.",
        };

        public const int COMPILE_START_TIMEOUT_MS = 5000;
        public const int COMPILE_START_POLL_INTERVAL_MS = 100;
        public const int COMPILE_FINISH_MISSED_CALLBACK_GRACE_MS = 2000;
        public const int EDITOR_FRAME_WAIT_TIMEOUT_MS = 5000;
        
        public const int MAX_SETTINGS_SIZE_BYTES = 1024 * 16;
        public const string SECURITY_LOG_PREFIX = "[UnityCliLoop Security]";
        
        public static string GenerateCorrelationId()
        {
            return System.Guid.NewGuid().ToString(GUID_FORMAT_NO_HYPHENS)[..CORRELATION_ID_LENGTH];
        }
    }
} 
