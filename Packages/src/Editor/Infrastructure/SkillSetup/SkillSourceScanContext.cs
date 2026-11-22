using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// The Editor values a skill source scan depends on, read up front so the scan itself touches only
    /// the file system and can run on any thread.
    /// </summary>
    internal sealed class SkillSourceScanContext
    {
        private const string EditorDirName = "Editor";
        private const string CliOnlyToolsDirName = "CliOnlyTools~";

        internal SkillSourceScanContext(
            string projectRoot,
            string cliOnlySkillSourceRoot,
            bool searchesCliOnlySkillSourceRoot,
            RuntimePlatform platform)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(cliOnlySkillSourceRoot), "cliOnlySkillSourceRoot must not be null or empty");

            ProjectRoot = projectRoot;
            CliOnlySkillSourceRoot = cliOnlySkillSourceRoot;
            SearchesCliOnlySkillSourceRoot = searchesCliOnlySkillSourceRoot;
            Platform = platform;
        }

        internal string ProjectRoot { get; }

        /// <summary>
        /// This package's CLI-only skill folder, which is scanned as a whole rather than through its
        /// Editor folders wherever it turns up as a search root.
        /// </summary>
        internal string CliOnlySkillSourceRoot { get; }

        /// <summary>
        /// Whether <see cref="CliOnlySkillSourceRoot"/> is itself a search root, which holds only when
        /// <see cref="ProjectRoot"/> is the project this Editor has open.
        /// </summary>
        internal bool SearchesCliOnlySkillSourceRoot { get; }

        internal RuntimePlatform Platform { get; }

        /// <summary>
        /// Reads the Editor values for a scan of <paramref name="projectRoot"/>. Call it on the main thread.
        /// </summary>
        internal static SkillSourceScanContext CaptureForProject(string projectRoot)
        {
            RuntimePlatform platform = UnityEngine.Application.platform;
            string cliOnlySkillSourceRoot = Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                EditorDirName,
                CliOnlyToolsDirName);
            bool searchesCliOnlySkillSourceRoot = SkillSourceRootEnumerator.HaveSamePathIdentityAtPlatform(
                projectRoot,
                UnityCliLoopPathResolver.GetProjectRoot(),
                platform);
            return new SkillSourceScanContext(
                projectRoot,
                cliOnlySkillSourceRoot,
                searchesCliOnlySkillSourceRoot,
                platform);
        }
    }
}
