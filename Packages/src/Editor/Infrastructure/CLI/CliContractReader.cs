using System.IO;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads package-owned CLI contracts so editor setup can inspect compatibility before binaries are installed.
    /// </summary>
    internal static class CliContractReader
    {
        public static string GetBundledRequiredDispatcherVersionFlag()
        {
            return GetRequiredDispatcherVersionFlag(UnityCliLoopConstants.PackageResolvedPath);
        }

        public static string GetBundledMinimumRequiredDispatcherVersion()
        {
            return GetMinimumRequiredDispatcherVersion(UnityCliLoopConstants.PackageResolvedPath);
        }

        internal static string GetMinimumRequiredDispatcherVersion(string packageResolvedPath)
        {
            JObject contract = ReadCoreContract(packageResolvedPath);
            return ReadRequiredString(
                contract,
                CliConstants.CLI_CONTRACT_MINIMUM_REQUIRED_DISPATCHER_VERSION_KEY);
        }

        internal static string GetRequiredDispatcherVersionFlag(string packageResolvedPath)
        {
            JObject contract = ReadCoreContract(packageResolvedPath);
            return ReadRequiredString(
                contract,
                CliConstants.CLI_CONTRACT_REQUIRED_DISPATCHER_VERSION_FLAG_KEY);
        }

        private static JObject ReadCoreContract(string packageResolvedPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(packageResolvedPath), "packageResolvedPath must not be null or empty");

            string path = Path.Combine(
                packageResolvedPath,
                CliConstants.CLI_PACKAGE_DIR_NAME,
                CliConstants.GO_CLI_CORE_DIR_NAME,
                CliConstants.CLI_CONTRACT_FILE_NAME);
            if (!File.Exists(path))
            {
                return null;
            }

            return JObject.Parse(File.ReadAllText(path));
        }

        private static string ReadRequiredString(JObject contract, string key)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(key), "key must not be null or empty");

            if (contract == null)
            {
                return null;
            }

            JToken token = contract[key];
            if (token == null || token.Type != JTokenType.String)
            {
                throw new InvalidDataException($"CLI contract must contain string field: {key}");
            }

            string value = token.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidDataException($"CLI contract field must not be empty: {key}");
            }
            return value;
        }
    }
}
