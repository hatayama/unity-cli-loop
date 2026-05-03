using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    // Keeps each Unity project on the package-owned CLI bundle without making the Install CLI button do that work.
    internal static class ProjectLocalCliAutoInstaller
    {
        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            if (Application.isBatchMode)
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
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            CliInstallResult result = EnsureProjectLocalCliCurrent(projectRoot, McpConstants.PackageInfo.version);
            if (result.Success)
            {
                return;
            }

            Debug.LogWarning(
                $"[{McpConstants.PROJECT_NAME}] Failed to update project-local uLoop CLI: {result.ErrorOutput}");
        }
    }
}
