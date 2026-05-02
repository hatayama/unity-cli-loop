namespace io.github.hatayama.uLoopMCP
{
    public static class CliConstants
    {
        public const string EXECUTABLE_NAME = "uloop";
        public const string VERSION_FLAG = "--version";
        public const int GLOBAL_INSTALL_TIMEOUT_MS = 30000;
        public const string RELEASE_DOWNLOAD_BASE_URL = "https://github.com/hatayama/unity-cli-loop/releases/download";
        public const string POSIX_INSTALL_SCRIPT_NAME = "install.sh";
        public const string WINDOWS_INSTALL_SCRIPT_NAME = "install.ps1";
        public const string INSTALL_VERSION_ENVIRONMENT_VARIABLE = "ULOOP_VERSION";
        public const string RELEASE_TAG_PREFIX = "v";
        public const string SKILL_DIR_PREFIX = "uloop-";
        public const string SKILL_DIR_GLOB = "uloop-*";
        public const string GO_CLI_PACKAGE_DIR_NAME = "GoCli~";
        public const string DIST_DIR_NAME = "dist";
        public const string PROJECT_LOCAL_BIN_DIR_NAME = "bin";
        public const string PROJECT_LOCAL_UNIX_COMMAND_NAME = "uloop-core";
        public const string PROJECT_LOCAL_WINDOWS_COMMAND_NAME = "uloop-core.exe";
    }
}
