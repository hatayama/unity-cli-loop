using System;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
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
        private const string PIN_JSON_DISPATCHER_RELEASE_TAG_KEY = "dispatcherReleaseTag";
        private const string PIN_JSON_DISPATCHER_ARCHIVE_MANIFEST_KEY = "dispatcherArchiveManifest";
        private const int SHA256_HEX_LENGTH = 64;
        private const int MANIFEST_ENTRY_PREFIX_LENGTH = SHA256_HEX_LENGTH + 2;

        public CliPinLoadResult LoadPackagePin()
        {
            string pinPath = Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
            return LoadPinFromPath(pinPath);
        }

        /// <summary>
        /// Reads the provenance-pinned dispatcher release inputs that are required before bootstrap execution.
        /// </summary>
        public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
        {
            string pinPath = Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
            return LoadDispatcherBootstrapPinFromPath(pinPath);
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
            string dispatcherReleaseTag = parsed[PIN_JSON_DISPATCHER_RELEASE_TAG_KEY]?.ToString();
            string dispatcherArchiveManifest = parsed[PIN_JSON_DISPATCHER_ARCHIVE_MANIFEST_KEY]?.ToString();
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
                new CliPin(
                    projectRunnerVersion,
                    minimumDispatcherVersion,
                    dispatcherReleaseTag,
                    dispatcherArchiveManifest));
        }

        /// <summary>
        /// Loads and validates the additional pin fields needed to bootstrap a dispatcher release safely.
        /// </summary>
        internal static DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPinFromPath(string pinPath)
        {
            CliPinLoadResult pinResult = LoadPinFromPath(pinPath);
            if (!pinResult.Success)
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(pinResult.ErrorMessage);
            }

            return ValidateDispatcherBootstrapPin(pinResult.Pin, pinPath);
        }

        private static DispatcherBootstrapPinLoadResult ValidateDispatcherBootstrapPin(CliPin pin, string pinPath)
        {
            bool hasReleaseTag = pin.DispatcherReleaseTag != null;
            bool hasArchiveManifest = pin.DispatcherArchiveManifest != null;
            if (!hasReleaseTag && !hasArchiveManifest)
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} is missing dispatcherReleaseTag and dispatcherArchiveManifest.");
            }
            if (!hasReleaseTag || !hasArchiveManifest)
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} must define both dispatcherReleaseTag and dispatcherArchiveManifest.");
            }
            if (string.IsNullOrWhiteSpace(pin.DispatcherReleaseTag)
                || string.IsNullOrWhiteSpace(pin.DispatcherArchiveManifest))
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} defines empty dispatcher bootstrap fields.");
            }
            if (!IsValidDispatcherReleaseTag(pin.DispatcherReleaseTag))
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} defines an invalid dispatcherReleaseTag.");
            }
            if (!IsValidArchiveManifest(pin.DispatcherArchiveManifest))
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} contains an invalid dispatcherArchiveManifest entry.");
            }
            if (!HasArchiveManifestEntry(pin.DispatcherArchiveManifest, CliConstants.POSIX_INSTALL_SCRIPT_NAME)
                || !HasArchiveManifestEntry(pin.DispatcherArchiveManifest, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME))
            {
                return DispatcherBootstrapPinLoadResult.FromFailure(
                    $"Unity CLI Loop pin file at {pinPath} is missing an installer script digest.");
            }

            return DispatcherBootstrapPinLoadResult.FromSuccess(
                pin.DispatcherReleaseTag,
                pin.DispatcherArchiveManifest);
        }

        private static bool IsValidDispatcherReleaseTag(string dispatcherReleaseTag)
        {
            if (dispatcherReleaseTag != dispatcherReleaseTag.Trim()
                || !dispatcherReleaseTag.StartsWith(CliConstants.DISPATCHER_RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return false;
            }
            foreach (char character in dispatcherReleaseTag)
            {
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '-'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsValidArchiveManifest(string archiveManifest)
        {
            if (archiveManifest.Contains("\r"))
            {
                return false;
            }
            string[] entries = archiveManifest.Split('\n');
            System.Collections.Generic.HashSet<string> assetNames = new();
            foreach (string entry in entries)
            {
                if (!IsValidArchiveManifestEntry(entry))
                {
                    return false;
                }
                string assetName = entry.Substring(MANIFEST_ENTRY_PREFIX_LENGTH);
                if (!assetNames.Add(assetName))
                {
                    return false;
                }
            }
            return entries.Length > 0;
        }

        private static bool IsValidArchiveManifestEntry(string entry)
        {
            if (entry.Length <= MANIFEST_ENTRY_PREFIX_LENGTH
                || entry[SHA256_HEX_LENGTH] != ' '
                || entry[SHA256_HEX_LENGTH + 1] != ' ')
            {
                return false;
            }

            for (int index = 0; index < SHA256_HEX_LENGTH; index++)
            {
                if (!IsHexCharacter(entry[index]))
                {
                    return false;
                }
            }

            string assetName = entry.Substring(MANIFEST_ENTRY_PREFIX_LENGTH);
            return assetName == assetName.Trim()
                && assetName.IndexOfAny(new char[] { '\r', '\n', '\t', ' ' }) < 0;
        }

        private static bool HasArchiveManifestEntry(string archiveManifest, string assetName)
        {
            string suffix = "  " + assetName;
            string[] entries = archiveManifest.Split('\n');
            foreach (string entry in entries)
            {
                if (entry.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsHexCharacter(char value)
        {
            return value >= '0' && value <= '9'
                || value >= 'a' && value <= 'f'
                || value >= 'A' && value <= 'F';
        }
    }
}
