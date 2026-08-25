namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the CLI and skill layout contract used by Unity CLI Loop.
    /// </summary>
    public static class CliConstants
    {
        public const string EXECUTABLE_NAME = "uloop";
        // Why: the runtime IPC gate compares this contract generation, not release numbers.
        // Bump it together with cli/common/clicontract/contract.json protocolVersion only when this package can
        // no longer interoperate with a different CLI protocol generation.
        public const int REQUIRED_CLI_PROTOCOL_VERSION = 5;
        public const string VERSION_FLAG = "--version";
        public const string SHORT_VERSION_FLAG = "-v";
        public const string JSON_FLAG = "--json";
        public const string RELEASE_DOWNLOAD_BASE_URL = "https://github.com/hatayama/unity-cli-loop/releases/download";
        public const string SCRIPTS_DIR_NAME = "scripts";
        public const string POSIX_INSTALL_SCRIPT_NAME = "install.sh";
        public const string WINDOWS_INSTALL_SCRIPT_NAME = "install.ps1";
        public const string INSTALL_DIR_ENVIRONMENT_VARIABLE = "ULOOP_INSTALL_DIR";
        public const string INSTALL_VERSION_ENVIRONMENT_VARIABLE = "ULOOP_VERSION";
        public const string ARCHIVE_MANIFEST_ENVIRONMENT_VARIABLE = "ULOOP_ARCHIVE_MANIFEST";
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
        public const string DISPATCHER_RELEASE_TAG_PREFIX = "dispatcher-v";
        public const string SKILL_DIR_PREFIX = "uloop-";
        public const string TEMPORARY_SKILLS_DIR_NAME = "TemporarySkills~";
        public const string V3_CLI_INVOCATION_MIGRATION_SKILL_NAME = "v3-cli-invocation-migration";
        public const string UNITY_PACKAGES_DIR_NAME = "Packages";
        public const string PACKAGE_SOURCE_DIR_NAME = "src";
        public const string HOMEBREW_CELLAR_DIR_NAME = "Cellar";
        public const string HOMEBREW_UPGRADE_COMMAND = "brew upgrade " + EXECUTABLE_NAME;
        public const string HOMEBREW_REINSTALL_COMMAND = "brew reinstall " + EXECUTABLE_NAME;
        public const string GLOBAL_UNIX_COMMAND_NAME = EXECUTABLE_NAME;
        public const string GLOBAL_WINDOWS_COMMAND_NAME = EXECUTABLE_NAME + ".exe";
    }
}
