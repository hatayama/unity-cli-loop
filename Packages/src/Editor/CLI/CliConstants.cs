namespace io.github.hatayama.UnityCliLoop
{
    public static class CliConstants
    {
        public const string EXECUTABLE_NAME = "uloop";
        public const string VERSION_FLAG = "--version";
        public const string RELEASE_DOWNLOAD_BASE_URL = "https://github.com/hatayama/unity-cli-loop/releases/download";
        public const string POSIX_INSTALL_SCRIPT_NAME = "install.sh";
        public const string WINDOWS_INSTALL_SCRIPT_NAME = "install.ps1";
        public const string INSTALL_DIR_ENVIRONMENT_VARIABLE = "ULOOP_INSTALL_DIR";
        public const string INSTALL_VERSION_ENVIRONMENT_VARIABLE = "ULOOP_VERSION";
        public const string REMOVE_LEGACY_ENVIRONMENT_VARIABLE = "ULOOP_REMOVE_LEGACY";
        public const string REMOVE_LEGACY_ENABLED_VALUE = "1";
        public const string POSIX_HOME_ENVIRONMENT_VARIABLE = "HOME";
        public const string POSIX_PATH_ENVIRONMENT_VARIABLE = "PATH";
        public const string WINDOWS_APPDATA_ENVIRONMENT_VARIABLE = "APPDATA";
        public const string WINDOWS_LOCAL_APPDATA_ENVIRONMENT_VARIABLE = "LOCALAPPDATA";
        public const string WINDOWS_PATH_ENVIRONMENT_VARIABLE = "Path";
        public const string POSIX_LOCAL_DIR_NAME = ".local";
        public const string WINDOWS_PROGRAMS_DIR_NAME = "Programs";
        public const string WINDOWS_NODE_GLOBAL_BIN_DIR_NAME = "npm";
        public const string NATIVE_INSTALL_DIR_NAME = "uloop";
        public const string NATIVE_INSTALL_BIN_DIR_NAME = "bin";
        public const string POSIX_PATH_SEPARATOR = ":";
        public const string WINDOWS_PATH_SEPARATOR = ";";
        public const string RELEASE_TAG_PREFIX = "v";
        public const string SKILL_DIR_PREFIX = "uloop-";
        public const string SKILL_DIR_GLOB = "uloop-*";
        public const string GO_CLI_PACKAGE_DIR_NAME = "GoCli~";
        public const string DIST_DIR_NAME = "dist";
        public const string GLOBAL_UNIX_COMMAND_NAME = EXECUTABLE_NAME;
        public const string GLOBAL_WINDOWS_COMMAND_NAME = EXECUTABLE_NAME + ".exe";
        public const string WINDOWS_CMD_SHIM_NAME = EXECUTABLE_NAME + ".cmd";
        public const string WINDOWS_POWERSHELL_SHIM_NAME = EXECUTABLE_NAME + ".ps1";
        public const string GLOBAL_DISPATCHER_UNIX_BUNDLE_NAME = "uloop-dispatcher";
        public const string GLOBAL_DISPATCHER_WINDOWS_BUNDLE_NAME = "uloop-dispatcher.exe";
        public const string LEGACY_TYPESCRIPT_PACKAGE_NAME = "uloop-cli";
        public const string PROJECT_LOCAL_BIN_DIR_NAME = "bin";
        public const string PROJECT_LOCAL_UNIX_COMMAND_NAME = "uloop-core";
        public const string PROJECT_LOCAL_WINDOWS_COMMAND_NAME = "uloop-core.exe";
    }
}
