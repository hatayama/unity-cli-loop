using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Describes pending V3 custom tool migration work found in the Unity project.
    /// </summary>
    public readonly struct ThirdPartyToolMigrationPreview
    {
        private readonly string[] _filePaths;

        public ThirdPartyToolMigrationPreview(int fileCount, int replacementCount, string[] filePaths)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");
            Debug.Assert(filePaths != null, "filePaths must not be null");

            FileCount = fileCount;
            ReplacementCount = replacementCount;
            _filePaths = ThirdPartyToolMigrationFilePathSnapshot.Copy(filePaths);
        }

        public int FileCount { get; }
        public int ReplacementCount { get; }
        public string[] FilePaths => ThirdPartyToolMigrationFilePathSnapshot.Copy(_filePaths);
        public bool HasTargets => FileCount > 0;
    }

    /// <summary>
    /// Reports migration progress so editor UI can repaint while project files are inspected.
    /// </summary>
    public readonly struct ThirdPartyToolMigrationProgress
    {
        public ThirdPartyToolMigrationProgress(int processedItemCount, int totalItemCount)
        {
            Debug.Assert(processedItemCount >= 0, "processedItemCount must not be negative");
            Debug.Assert(totalItemCount >= 0, "totalItemCount must not be negative");
            Debug.Assert(
                processedItemCount <= totalItemCount || totalItemCount == 0,
                "processedItemCount must not exceed totalItemCount");

            ProcessedItemCount = processedItemCount;
            TotalItemCount = totalItemCount;
        }

        public int ProcessedItemCount { get; }
        public int TotalItemCount { get; }
    }

    /// <summary>
    /// Describes the files rewritten by the V3 custom tool migration workflow.
    /// </summary>
    public readonly struct ThirdPartyToolMigrationResult
    {
        private readonly string[] _filePaths;

        public ThirdPartyToolMigrationResult(int fileCount, int replacementCount, string[] filePaths)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");
            Debug.Assert(filePaths != null, "filePaths must not be null");

            FileCount = fileCount;
            ReplacementCount = replacementCount;
            _filePaths = ThirdPartyToolMigrationFilePathSnapshot.Copy(filePaths);
        }

        public int FileCount { get; }
        public int ReplacementCount { get; }
        public string[] FilePaths => ThirdPartyToolMigrationFilePathSnapshot.Copy(_filePaths);
        public bool Changed => FileCount > 0;
    }

    public interface IThirdPartyToolMigrationPort
    {
        ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot);
        Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct);

        /// <summary>
        /// Builds a preview scoped to the assemblies containing the given seed files (e.g. compile-
        /// error-matched migration targets), falling back to a full-project scan when the seeds do not
        /// resolve to a safe, complete scope (see ThirdPartyToolMigrationScanScopeResolver).
        /// </summary>
        Task<ThirdPartyToolMigrationPreview> PreviewMigrationForSeedFilesAsync(
            string projectRoot,
            List<string> seedFilePaths,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct);

        /// <summary>
        /// Checks whether the most recent compile failure was caused by V2 legacy custom-tool APIs, by
        /// matching Unity Console error text against known legacy tokens (mirrors Unity's own API
        /// Updater, which performs the same kind of compile-error-driven detection). Returns
        /// Found == false with an empty TargetFilePaths (never null) without inspecting the console
        /// when no compile failure is in effect.
        /// </summary>
        (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(string projectRoot);

        Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct);
        ThirdPartyToolMigrationResult ApplyMigration(string projectRoot);
        Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct);
    }

    /// <summary>
    /// Creates stable migration file path snapshots before value objects store caller-owned arrays.
    /// </summary>
    internal static class ThirdPartyToolMigrationFilePathSnapshot
    {
        internal static string[] Copy(string[] filePaths)
        {
            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            if (filePaths.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] copy = new string[filePaths.Length];
            Array.Copy(filePaths, copy, filePaths.Length);
            return copy;
        }
    }
}
