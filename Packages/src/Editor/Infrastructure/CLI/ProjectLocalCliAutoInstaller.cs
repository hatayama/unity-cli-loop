using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Keeps each Unity project on the package-owned CLI bundle without making the Install CLI button do that work.
    /// <summary>
    /// Provides Project Local CLI Auto Installer behavior for Unity CLI Loop.
    /// </summary>
    internal static class ProjectLocalCliAutoInstaller
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

            EditorApplication.delayCall += EnsureProjectLocalCliForCurrentProject;
        }

        internal static CliInstallResult EnsureProjectLocalCliCurrent(string projectRoot, string expectedVersion)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(expectedVersion), "expectedVersion must not be null or empty");

            string sourceBundlePath = ProjectLocalCliInstaller.GetProjectCliBundlePath();
            return EnsureProjectLocalCliCurrentFromBundle(sourceBundlePath, projectRoot, expectedVersion);
        }

        internal static CliInstallResult EnsureProjectLocalCliCurrentFromBundle(
            string sourceBundlePath,
            string projectRoot,
            string expectedVersion)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceBundlePath), "sourceBundlePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(expectedVersion), "expectedVersion must not be null or empty");

            if (ProjectLocalCliInstaller.IsProjectLocalCliCurrentForBundle(
                sourceBundlePath,
                projectRoot,
                expectedVersion))
            {
                return new CliInstallResult(true, "");
            }

            return ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(sourceBundlePath, projectRoot);
        }

        private static void EnsureProjectLocalCliForCurrentProject()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            CliInstallResult result = EnsureProjectLocalCliCurrent(projectRoot, UnityCliLoopConstants.PackageInfo.version);
            if (result.Success)
            {
                return;
            }

            Debug.LogWarning(
                $"[{UnityCliLoopConstants.PROJECT_NAME}] Failed to update project-local uLoop CLI: {result.ErrorOutput}");
        }
    }
}
