using System.IO;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
    /// Reads the CLI pin JSON that ships with the Unity package so callers can
    /// consult a single source of truth for minimum-version requirements.
    /// </summary>
    public static class CliPinReader
    {
        // Why: keep pin JSON keys named in one place so callers do not spread string literals.
        private const string PIN_JSON_PROJECT_RUNNER_VERSION_KEY = "projectRunnerVersion";
        private const string PIN_JSON_MINIMUM_DISPATCHER_VERSION_KEY = "minimumDispatcherVersion";

        public static CliPinLoadResult LoadPackagePin()
        {
            string pinPath = Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
            return LoadPinFromPath(pinPath);
        }

        // Why: exposed so tests and edge-case callers can point at an alternate pin file layout.
        public static CliPinLoadResult LoadPinFromPath(string pinPath)
        {
            if (string.IsNullOrWhiteSpace(pinPath))
            {
                return CliPinLoadResult.FromFailure("Unity CLI Loop pin file path is empty.");
            }

            if (!File.Exists(pinPath))
            {
                return CliPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file not found at {pinPath}.");
            }

            string content = File.ReadAllText(pinPath);
            if (string.IsNullOrWhiteSpace(content))
            {
                return CliPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} is empty.");
            }

            JObject parsed = JObject.Parse(content);
            string projectRunnerVersion = parsed[PIN_JSON_PROJECT_RUNNER_VERSION_KEY]?.ToString();
            string minimumDispatcherVersion = parsed[PIN_JSON_MINIMUM_DISPATCHER_VERSION_KEY]?.ToString();
            if (string.IsNullOrWhiteSpace(projectRunnerVersion))
            {
                return CliPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} is missing {PIN_JSON_PROJECT_RUNNER_VERSION_KEY}.");
            }
            if (string.IsNullOrWhiteSpace(minimumDispatcherVersion))
            {
                return CliPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} is missing {PIN_JSON_MINIMUM_DISPATCHER_VERSION_KEY}.");
            }

            return CliPinLoadResult.FromSuccess(
                new CliPin(projectRunnerVersion, minimumDispatcherVersion));
        }

        // Why: composes the dispatcher release tag from the pin so callers do not have to know the prefix.
        public static string BuildDispatcherReleaseTag(string minimumDispatcherVersion)
        {
            return CliConstants.DISPATCHER_RELEASE_TAG_PREFIX + minimumDispatcherVersion;
        }
    }
}
