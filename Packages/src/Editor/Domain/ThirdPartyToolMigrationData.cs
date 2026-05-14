using System;
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
        public ThirdPartyToolMigrationPreview(int fileCount, int replacementCount, string[] filePaths)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");
            Debug.Assert(filePaths != null, "filePaths must not be null");

            FileCount = fileCount;
            ReplacementCount = replacementCount;
            FilePaths = filePaths ?? Array.Empty<string>();
        }

        public int FileCount { get; }
        public int ReplacementCount { get; }
        public string[] FilePaths { get; }
        public bool HasTargets => FileCount > 0;
    }

    /// <summary>
    /// Reports preview scan progress so editor UI can repaint while project files are inspected.
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
        public ThirdPartyToolMigrationResult(int fileCount, int replacementCount, string[] filePaths)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");
            Debug.Assert(filePaths != null, "filePaths must not be null");

            FileCount = fileCount;
            ReplacementCount = replacementCount;
            FilePaths = filePaths ?? Array.Empty<string>();
        }

        public int FileCount { get; }
        public int ReplacementCount { get; }
        public string[] FilePaths { get; }
        public bool Changed => FileCount > 0;
    }

    public interface IThirdPartyToolMigrationPort
    {
        ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot);
        Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct);
        ThirdPartyToolMigrationResult ApplyMigration(string projectRoot);
    }
}
