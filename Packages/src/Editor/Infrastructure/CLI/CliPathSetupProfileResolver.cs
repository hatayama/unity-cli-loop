using System;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Adapts Unity and OS state into the pure CLI PATH setup profile resolver.
    /// </summary>
    internal static class CliPathSetupProfileResolver
    {
        public static CliPathSetupPlan ResolveCurrentUserPlan(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstaller.GetCurrentUserGlobalCliInstallDirectory(platform);
            string shellPath = NodeEnvironmentResolver.GetUserShell();
            return ResolvePlan(
                platform,
                shellPath,
                Environment.GetEnvironmentVariable(CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE),
                Environment.GetEnvironmentVariable("ZDOTDIR"),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                installDirectory,
                File.Exists);
        }

        internal static CliPathSetupPlan ResolvePlan(
            RuntimePlatform platform,
            string shellPath,
            string homeDirectory,
            string zDotDirectory,
            string xdgConfigHome,
            string installDirectory,
            Func<string, bool> fileExists)
        {
            Debug.Assert(fileExists != null, "fileExists must not be null");

            string resolvedHomeDirectory = ResolveHomeDirectory(homeDirectory);
            return io.github.hatayama.UnityCliLoop.Domain.CliPathSetupProfileResolver.ResolvePlan(
                ToCliPathSetupPlatform(platform),
                shellPath,
                resolvedHomeDirectory,
                zDotDirectory,
                xdgConfigHome,
                installDirectory,
                fileExists);
        }

        private static CliPathSetupPlatform ToCliPathSetupPlatform(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliPathSetupPlatform.Windows
                : CliPathSetupPlatform.Posix;
        }

        private static string ResolveHomeDirectory(string homeDirectory)
        {
            return string.IsNullOrWhiteSpace(homeDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : homeDirectory;
        }
    }
}
