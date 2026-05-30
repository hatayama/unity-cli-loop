namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the CLI and skill layout contract used by Unity CLI Loop.
    /// </summary>
    public static class CliConstants
    {
        public const string EXECUTABLE_NAME = "uloop";
        public const string MINIMUM_REQUIRED_CLI_VERSION = "3.0.0-beta.20";
        public const string MINIMUM_REQUIRED_CLI_RELEASE_TAG = CLI_RELEASE_TAG_PREFIX + MINIMUM_REQUIRED_CLI_VERSION;
        public const string VERSION_FLAG = "--version";
        public const string SHORT_VERSION_FLAG = "-v";
        public const string RAW_CONTENT_BASE_URL = "https://raw.githubusercontent.com/hatayama/unity-cli-loop";
        public const string SCRIPTS_DIR_NAME = "scripts";
        public const string POSIX_INSTALL_SCRIPT_NAME = "install.sh";
        public const string WINDOWS_INSTALL_SCRIPT_NAME = "install.ps1";
        public const string INSTALL_DIR_ENVIRONMENT_VARIABLE = "ULOOP_INSTALL_DIR";
        public const string INSTALL_VERSION_ENVIRONMENT_VARIABLE = "ULOOP_VERSION";
        public const string POSIX_HOME_ENVIRONMENT_VARIABLE = "HOME";
        public const string POSIX_PATH_ENVIRONMENT_VARIABLE = "PATH";
        public const string WINDOWS_LOCAL_APPDATA_ENVIRONMENT_VARIABLE = "LOCALAPPDATA";
        public const string WINDOWS_PATH_ENVIRONMENT_VARIABLE = "Path";
        public const string POSIX_SHELL_EXECUTABLE_PATH = "/bin/sh";
        public const string POSIX_LOCAL_DIR_NAME = ".local";
        public const string WINDOWS_PROGRAMS_DIR_NAME = "Programs";
        public const string NATIVE_INSTALL_DIR_NAME = "uloop";
        public const string NATIVE_INSTALL_BIN_DIR_NAME = "bin";
        public const string POSIX_PATH_SEPARATOR = ":";
        public const string WINDOWS_PATH_SEPARATOR = ";";
        public const string RELEASE_TAG_PREFIX = "v";
        public const string CLI_RELEASE_TAG_PREFIX = "cli-v";
        public const string BETA_VERSION_MARKER = "-beta.";
        public const string SKILL_DIR_PREFIX = "uloop-";
        public const string UNITY_PACKAGES_DIR_NAME = "Packages";
        public const string PACKAGE_SOURCE_DIR_NAME = "src";
        public const string CLI_PACKAGE_DIR_NAME = "Cli~";
        public const string LEGACY_GO_CLI_PACKAGE_DIR_NAME = "GoCli~";
        public const string DIST_DIR_NAME = "dist";
        public const string WINDOWS_AMD64_DIST_DIR_NAME = "windows-amd64";
        public const string CLI_LAYOUT_CONTRACT_FILE_NAME = "layout-contract.json";
        public const string CLI_CONTRACT_FILE_NAME = "contract.json";
        public const string GLOBAL_UNIX_COMMAND_NAME = EXECUTABLE_NAME;
        public const string GLOBAL_WINDOWS_COMMAND_NAME = EXECUTABLE_NAME + ".exe";
    }
}
