using System.Threading;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Keeps the global native CLI new enough for this Unity package during editor startup.
    /// </summary>
    internal static class GlobalCliAutoInstaller
    {
        internal static void ScheduleForEditorStartup()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            if (UnityEngine.Application.isBatchMode)
            {
                return;
            }

            EditorApplication.delayCall += EnsureGlobalCliForCurrentProject;
        }

        private static async void EnsureGlobalCliForCurrentProject()
        {
            CliInstallResult result = await CliSetupApplicationFacade.EnsureGlobalCliCurrentAsync(
                UnityEngine.Application.platform,
                CancellationToken.None);
            if (result.Success)
            {
                return;
            }

            Debug.LogWarning(
                $"[{UnityCliLoopConstants.PROJECT_NAME}] Failed to update the global uLoop CLI. Retry Install CLI when network access to GitHub is available: {result.ErrorOutput}");
        }
    }
}
