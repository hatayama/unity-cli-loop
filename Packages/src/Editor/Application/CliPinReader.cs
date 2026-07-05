using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Immutable snapshot of the CLI pin JSON shipped with the package.
    /// </summary>
    public readonly struct CliPin
    {
        public CliPin(string projectRunnerVersion, string minimumDispatcherVersion)
        {
            ProjectRunnerVersion = projectRunnerVersion;
            MinimumDispatcherVersion = minimumDispatcherVersion;
        }

        public string ProjectRunnerVersion { get; }
        public string MinimumDispatcherVersion { get; }
    }

    /// <summary>
    /// Result of loading the CLI pin JSON.
    /// </summary>
    public readonly struct CliPinLoadResult
    {
        private CliPinLoadResult(bool success, CliPin pin, string errorMessage)
        {
            Success = success;
            Pin = pin;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public CliPin Pin { get; }
        public string ErrorMessage { get; }

        public static CliPinLoadResult FromSuccess(CliPin pin)
        {
            return new CliPinLoadResult(true, pin, string.Empty);
        }

        public static CliPinLoadResult FromFailure(string errorMessage)
        {
            return new CliPinLoadResult(false, default, errorMessage);
        }
    }

    /// <summary>
    /// Defines how the CLI pin JSON shipped with the Unity package is read, so Application code can
    /// consult a single source of truth for minimum-version requirements without depending on file IO.
    /// </summary>
    public interface ICliPinReader
    {
        CliPinLoadResult LoadPackagePin();
        string LoadMinimumDispatcherVersionOrThrow();
    }

    /// <summary>
    /// Builds derived values from the CLI pin JSON that ships with the Unity package.
    /// </summary>
    public static class CliPinReader
    {
        // Why: composes the dispatcher release tag from the pin so callers do not have to know the prefix.
        public static string BuildDispatcherReleaseTag(string minimumDispatcherVersion)
        {
            return CliConstants.DISPATCHER_RELEASE_TAG_PREFIX + minimumDispatcherVersion;
        }
    }
}
