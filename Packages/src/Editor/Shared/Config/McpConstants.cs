namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Central constants repository for Unity MCP system.
    ///
    /// Design document reference: Packages/src/Editor/ARCHITECTURE.md
    ///
    /// Related classes:
    /// - McpConfigService: Uses these constants for configuration management
    /// - McpServerConfigFactory: Uses port and environment variable constants
    /// - McpEditorWindow: Uses SessionState keys for UI state persistence
    /// - McpSessionManager: Uses SessionState keys for connection state management
    /// - EditorConfigProvider: Provides client names via GetClientNameForEditor method
    /// </summary>
    public static class McpConstants
    {
        private static UnityEditor.PackageManager.PackageInfo _cachedPackageInfo;

        /// <summary>
        /// Gets the PackageInfo for uLoopMCP package.
        /// Results are cached for performance.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown when package info cannot be resolved.</exception>
        public static UnityEditor.PackageManager.PackageInfo PackageInfo
        {
            get
            {
                if (_cachedPackageInfo == null)
                {
                    _cachedPackageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                        typeof(McpConstants).Assembly);

                    if (_cachedPackageInfo == null)
                    {
                        throw new System.InvalidOperationException(
                            "Failed to resolve PackageInfo for uLoopMCP. " +
                            "Ensure the package is properly installed via Package Manager.");
                    }
                }
                return _cachedPackageInfo;
            }
        }

        /// <summary>
        /// Gets the package name (e.g., "io.github.hatayama.uloopmcp").
        /// </summary>
        public static string PackageName => PackageInfo.name;

        /// <summary>
        /// Gets the Unity asset path for the package (e.g., "Packages/io.github.hatayama.uloopmcp").
        /// Use this for AssetDatabase.LoadAssetAtPath().
        /// </summary>
        public static string PackageAssetPath => PackageInfo.assetPath;

        /// <summary>
        /// Gets the resolved file system path for the package.
        /// Use this for file system operations.
        /// </summary>
        public static string PackageResolvedPath => PackageInfo.resolvedPath;

        /// <summary>
        /// Gets the package name pattern for directory searching (e.g., "io.github.hatayama.uloopmcp@*").
        /// Use this for Directory.GetDirectories() in PackageCache.
        /// </summary>
        public static string PackageNamePattern => $"{PackageName}@*";

        /// <summary>
        /// Gets the C# namespace for uLoopMCP (e.g., "io.github.hatayama.uLoopMCP").
        /// </summary>
        public static string PackageNamespace => typeof(McpConstants).Namespace;

        public const string PROJECT_NAME = "uLoopMCP";
        
        // JSON configuration keys
        public const string JSON_KEY_MCP_SERVERS = "mcpServers";
        public const string JSON_KEY_COMMAND = "command";
        public const string JSON_KEY_ARGS = "args";
        public const string JSON_KEY_ENV = "env";
        
        // Editor settings
        public const string SETTINGS_FILE_NAME = "UnityMcpSettings.json";
        public const string USER_SETTINGS_FOLDER = "UserSettings";
        
        // Server configuration
        public const string NODE_COMMAND = "node";
        public const string UNITY_TCP_PORT_ENV_KEY = "UNITY_TCP_PORT";
        
        // Scripting define symbols
        public const string SCRIPTING_DEFINE_ULOOPMCP_DEBUG = "ULOOPMCP_DEBUG";
        
        // Environment variable keys for development mode
        public const string ENV_KEY_ULOOPMCP_DEBUG = "ULOOPMCP_DEBUG";
        public const string ENV_KEY_ULOOPMCP_PRODUCTION = "ULOOPMCP_PRODUCTION";
        public const string ENV_KEY_MCP_DEBUG = "MCP_DEBUG";
        public const string ENV_KEY_NODE_OPTIONS = "NODE_OPTIONS";
        // MCP_CLIENT_NAME removed - now using clientInfo.name from MCP protocol
        
        // Environment variable values
        public const string ENV_VALUE_TRUE = "true";
        public const string NODE_OPTIONS_ENABLE_SOURCE_MAPS = "--enable-source-maps";
        
        // Client names for different editors
        public const string CLIENT_NAME_CURSOR = "Cursor";
        public const string CLIENT_NAME_CLAUDE_CODE = "Claude Code";
        public const string CLIENT_NAME_VSCODE = "VSCode";
        public const string CLIENT_NAME_GEMINI_CLI = "Gemini CLI";
        public const string CLIENT_NAME_WINDSURF = "Windsurf";
        public const string CLIENT_NAME_CODEX = "Codex";
        public const string CLIENT_NAME_MCP_INSPECTOR = "MCP Inspector";
        public const string UNKNOWN_CLIENT_NAME = "Unknown Client";
        
        // Command messages
        public const string CLIENT_SUCCESS_MESSAGE_TEMPLATE = "Client name registered successfully: {0}";
        
        // Reconnection settings
        public const int RECONNECTION_TIMEOUT_SECONDS = 10;
        
        // TypeScript server related constants
        public const string TYPESCRIPT_SERVER_DIR = "TypeScriptServer~";
        public const string DIST_DIR = "dist";
        public const string SERVER_BUNDLE_FILE = "server.bundle.js";
        
        // Package path constants
        public const string PACKAGES_DIR = "Packages";
        public const string SRC_DIR = "src";
        public const string LIBRARY_DIR = "Library";
        public const string TEMP_DIR = "Temp";
        public const string PACKAGE_CACHE_DIR = "PackageCache";
        public const string ULOOPMCP_DIR = "uLoopMCP";
        public const string COMPILE_RESULTS_DIR = "compile-results";
        public const string JSON_FILE_EXTENSION = ".json";
        
        // .uloop directory
        public const string ULOOP_DIR = ".uloop";
        public const string ULOOP_SETTINGS_FILE_NAME = "settings.permissions.json";
        public const string ULOOP_TOOL_SETTINGS_FILE_NAME = "settings.tools.json";

        // Tool name constants
        public const string TOOL_NAME_EXECUTE_DYNAMIC_CODE = "execute-dynamic-code";
        public const string TOOL_NAME_RUN_TESTS = "run-tests";

        // File output directories
        public const string OUTPUT_ROOT_DIR = ".uloop/outputs";
        public const string TEST_RESULTS_DIR = "TestResults";
        public const string SEARCH_RESULTS_DIR = "SearchResults";
        public const string HIERARCHY_RESULTS_DIR = "HierarchyResults";
        public const string FIND_GAMEOBJECTS_RESULTS_DIR = "FindGameObjectsResults";
        public const string SCREENSHOTS_DIR = "Screenshots";
        public const string VIBE_LOGS_DIR = "VibeLogs";

        // Correlation ID constants
        public const int CORRELATION_ID_LENGTH = 8; // Length for correlation ID generation
        public const string GUID_FORMAT_NO_HYPHENS = "N"; // GUID format without hyphens
        
        public const string ERROR_MESSAGE_DUPLICATE_ASMDEF = "Duplicate asmdef assembly name detected. Unity may not start compilation until duplicates are removed.";
        
        public const string ERROR_MESSAGE_EXECUTION_IN_PROGRESS = "Another execution is already in progress";
        public const string ERROR_MESSAGE_EXECUTION_CANCELLED = "Execution was cancelled or timed out";
        public const string ERROR_MESSAGE_NO_COMPILED_ASSEMBLY = "No compiled assembly provided";
        public const string ERROR_MESSAGE_NO_EXECUTE_METHOD = "No Execute method found in compiled assembly";
        public const string ERROR_MESSAGE_FAILED_TO_CREATE_INSTANCE = "Failed to create instance of target type";
        public const string ERROR_MESSAGE_UNSUPPORTED_SIGNATURE = "Execute method signature not supported";
        
        // Test constants
        public const int TEST_COMPILE_TIMEOUT_MS = 5000; // 5 seconds for test compilation

        // Screenshot coordinate system values
        public const string COORDINATE_SYSTEM_GAME_VIEW = "gameView";
        public const string COORDINATE_SYSTEM_WINDOW = "window";

        // SimulateMouseUi constants
        public const float SIMULATE_MOUSE_UI_DEFAULT_DRAG_SPEED = 2000f;

        // Compile tool constants
        public const int COMPILE_START_TIMEOUT_MS = 5000; // Timeout to detect "compile did not start"
        public const int COMPILE_START_POLL_INTERVAL_MS = 100;
        public const int COMPILE_DOMAIN_RELOAD_WAIT_TIMEOUT_MS = 10000;
        public const int COMPILE_DOMAIN_RELOAD_WAIT_POLL_INTERVAL_MS = 100;
        
        // Security constants
        public const int MAX_JSON_SIZE_BYTES = 1024 * 1024; // 1MB limit for JSON files
        public const int MAX_SETTINGS_SIZE_BYTES = 1024 * 16; // 16KB limit for settings files
        public const string SECURITY_LOG_PREFIX = "[uLoopMCP Security]";
        
        // Security: Allowed namespaces for reflection operations
        public static readonly string[] ALLOWED_NAMESPACES = {
            "UnityEditor",
            "Unity.EditorCoroutines",
            "Unity.VisualScripting",
            PackageNamespace
        };
        
        // Security: Denied types for reflection operations
        public static readonly string[] DENIED_SYSTEM_TYPES = {
            "System.Diagnostics.Process",
            "System.IO.File",
            "System.IO.Directory", 
            "System.Reflection.Assembly",
            "System.Activator"
        };
        
        /// <summary>
        /// Generates a short correlation ID for logging and tracking
        /// </summary>
        /// <returns>8-character correlation ID</returns>
        public static string GenerateCorrelationId()
        {
            return System.Guid.NewGuid().ToString(GUID_FORMAT_NO_HYPHENS)[..CORRELATION_ID_LENGTH];
        }
    }
} 
