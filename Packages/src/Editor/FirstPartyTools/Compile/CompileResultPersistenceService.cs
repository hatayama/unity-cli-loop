using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Persist compile responses for delayed retrieval after domain reload.
    /// </summary>
    public static class CompileResultPersistenceService
    {
        // Concurrent clients may still be waiting on recent result files.
        // Only delete files older than this threshold (longer than the 90-second wait timeout)
        // to avoid destroying results that active waiters need.
        private static readonly TimeSpan StaleResultThreshold = TimeSpan.FromMinutes(2);

        private static string ProjectRootPath => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        private static string CompileResultDirectoryPath => Path.Combine(
            ProjectRootPath,
            UnityCliLoopConstants.TEMP_DIR,
            UnityCliLoopConstants.UNITYCLILOOP_DIR,
            UnityCliLoopConstants.COMPILE_RESULTS_DIR
        );

        public static void ClearStaleResults()
        {
            if (!Directory.Exists(CompileResultDirectoryPath))
            {
                return;
            }

            string searchPattern = $"*{UnityCliLoopConstants.JSON_FILE_EXTENSION}";
            string[] resultFiles = Directory.GetFiles(CompileResultDirectoryPath, searchPattern);
            DateTime staleThreshold = DateTime.UtcNow - StaleResultThreshold;

            foreach (string resultFilePath in resultFiles)
            {
                FileInfo fileInfo = new(resultFilePath);
                if (fileInfo.LastWriteTimeUtc < staleThreshold)
                {
                    File.Delete(resultFilePath);
                }
            }
        }

        public static void SaveResult(string requestId, UnityCliLoopCompileResult response)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or empty");
            Debug.Assert(response != null, "response must not be null");

            if (!Directory.Exists(CompileResultDirectoryPath))
            {
                Directory.CreateDirectory(CompileResultDirectoryPath);
            }

            string sanitizedFileName = Path.GetFileName(requestId);
            Debug.Assert(sanitizedFileName == requestId,
                $"requestId must not contain path separators: '{requestId}'");

            string resultJson = JsonConvert.SerializeObject(response, Formatting.None);
            string fileName = $"{sanitizedFileName}{UnityCliLoopConstants.JSON_FILE_EXTENSION}";
            string filePath = Path.Combine(CompileResultDirectoryPath, fileName);
            File.WriteAllText(filePath, resultJson, Encoding.UTF8);
        }
    }
}
