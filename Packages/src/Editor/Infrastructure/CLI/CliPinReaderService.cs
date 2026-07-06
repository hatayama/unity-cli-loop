using System;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads the CLI pin JSON that ships with the Unity package from disk.
    /// </summary>
    public sealed class CliPinReaderService : ICliPinReader
    {
        // Why: keep pin JSON keys named in one place so callers do not spread string literals.
        private const string PIN_JSON_PROJECT_RUNNER_VERSION_KEY = "projectRunnerVersion";
        private const string PIN_JSON_MINIMUM_DISPATCHER_VERSION_KEY = "minimumDispatcherVersion";

        public CliPinLoadResult LoadPackagePin()
        {
            string pinPath = Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
            return LoadPinFromPath(pinPath);
        }

        // Why: setup/detection paths must fail closed when the pin is unreadable rather than defaulting
        // to "compatible", so both call sites share one Fail-Fast helper instead of duplicating it.
        public string LoadMinimumDispatcherVersionOrThrow()
        {
            CliPinLoadResult pinResult = LoadPackagePin();
            if (!pinResult.Success)
            {
                throw new InvalidOperationException(
                    "Unity CLI Loop cannot resolve minimum dispatcher version from the package pin: "
                    + pinResult.ErrorMessage);
            }
            return pinResult.Pin.MinimumDispatcherVersion;
        }

        // Why: internal so tests can point at an alternate pin file layout without exposing path-based
        // loading on the public ICliPinReader surface.
        internal static CliPinLoadResult LoadPinFromPath(string pinPath)
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

            // Why: a corrupt pin must surface as a structured failure like every other unreadable-pin
            // case, not as a raw parse exception; mirrors the JsonReaderException handling in
            // JsonRpcRequestProcessor and keeps LoadMinimumDispatcherVersionOrThrow's fail-closed message useful.
            JObject parsed;
            try
            {
                parsed = JObject.Parse(content);
            }
            catch (JsonReaderException ex)
            {
                return CliPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} contains invalid JSON: {ex.Message}");
            }

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
    }
}
