using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Carries the shell-specific target needed to add the native CLI install directory to PATH.
    /// </summary>
    public readonly struct CliPathSetupPlan
    {
        public CliPathSetupPlan(
            CliPathSetupShellKind shellKind,
            string shellName,
            bool canApplyAutomatically,
            string installDirectory,
            string profileInstallDirectory,
            string configurationFilePath,
            string configurationLine,
            string manualCommand)
        {
            if (canApplyAutomatically)
            {
                Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
                Debug.Assert(!string.IsNullOrWhiteSpace(profileInstallDirectory), "profileInstallDirectory must not be null or empty");
                Debug.Assert(!string.IsNullOrWhiteSpace(configurationFilePath), "configurationFilePath must not be null or empty");
                Debug.Assert(!string.IsNullOrWhiteSpace(configurationLine), "configurationLine must not be null or empty");
                Debug.Assert(!string.IsNullOrWhiteSpace(manualCommand), "manualCommand must not be null or empty");
            }

            ShellKind = shellKind;
            ShellName = shellName ?? string.Empty;
            CanApplyAutomatically = canApplyAutomatically;
            InstallDirectory = installDirectory ?? string.Empty;
            ProfileInstallDirectory = profileInstallDirectory ?? string.Empty;
            ConfigurationFilePath = configurationFilePath ?? string.Empty;
            ConfigurationLine = configurationLine ?? string.Empty;
            ManualCommand = manualCommand ?? string.Empty;
        }

        public CliPathSetupShellKind ShellKind { get; }
        public string ShellName { get; }
        public bool CanApplyAutomatically { get; }
        public string InstallDirectory { get; }
        public string ProfileInstallDirectory { get; }
        public string ConfigurationFilePath { get; }
        public string ConfigurationLine { get; }
        public string ManualCommand { get; }
    }

    public enum CliPathSetupShellKind
    {
        Unsupported,
        Zsh,
        Bash,
        Fish
    }

    public enum CliPathSetupApplyStatus
    {
        Applied,
        AlreadyConfigured,
        Unsupported,
        Failed
    }

    /// <summary>
    /// Reports the outcome of the profile append step, before terminal visibility is checked again.
    /// </summary>
    public readonly struct CliPathSetupApplyResult
    {
        public CliPathSetupApplyResult(
            bool success,
            CliPathSetupApplyStatus status,
            string errorOutput)
        {
            Success = success;
            Status = status;
            ErrorOutput = errorOutput ?? string.Empty;
        }

        public bool Success { get; }
        public CliPathSetupApplyStatus Status { get; }
        public string ErrorOutput { get; }
    }

    public enum CliPathSetupFlowStatus
    {
        AlreadyVisible,
        AppliedAndVisible,
        AlreadyConfiguredAndVisible,
        ManualSetupRequired,
        AppliedButStillMissing,
        Failed
    }

    /// <summary>
    /// Carries the user-facing result of making the CLI visible from a terminal shell.
    /// </summary>
    public readonly struct CliPathSetupFlowResult
    {
        public CliPathSetupFlowResult(
            CliPathSetupFlowStatus status,
            CliPathSetupPlan plan,
            string errorOutput)
        {
            Status = status;
            Plan = plan;
            ErrorOutput = errorOutput ?? string.Empty;
        }

        public CliPathSetupFlowStatus Status { get; }
        public CliPathSetupPlan Plan { get; }
        public string ErrorOutput { get; }
    }
}
