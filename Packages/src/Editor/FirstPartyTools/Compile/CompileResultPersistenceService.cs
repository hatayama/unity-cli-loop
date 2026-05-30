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
        private const string CompletedResultTempFileSuffix = ".tmp";
        private const string InProgressResultTempFileSuffix = ".tmp.write";

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

            string filePath = CreateResultFilePath(requestId);
            if (!Directory.Exists(CompileResultDirectoryPath))
            {
                Directory.CreateDirectory(CompileResultDirectoryPath);
            }

            string resultJson = JsonConvert.SerializeObject(response, Formatting.None);
            int resultBytes = Encoding.UTF8.GetByteCount(resultJson);
            VibeLogger.LogInfo(
                "compile_result_file_publish_start",
                "Publishing compile result file for CLI polling.",
                new
                {
                    request_id = requestId,
                    result_file_name = Path.GetFileName(filePath),
                    result_directory = CompileResultDirectoryPath,
                    result_directory_exists = Directory.Exists(CompileResultDirectoryPath),
                    result_bytes = resultBytes,
                    success = response.Success,
                    error_count = response.ErrorCount,
                    warning_count = response.WarningCount
                },
                requestId);
            PublishResultFile(filePath, resultJson);
            VibeLogger.LogInfo(
                "compile_result_file_publish_complete",
                "Published compile result file for CLI polling.",
                new
                {
                    request_id = requestId,
                    result_file_name = Path.GetFileName(filePath),
                    result_path = filePath,
                    result_exists = File.Exists(filePath),
                    result_bytes = resultBytes
                },
                requestId);
        }

        public static bool ResultExists(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or empty");

            string filePath = CreateResultFilePath(requestId);
            return File.Exists(filePath);
        }

        private static string CreateResultFilePath(string requestId)
        {
            ValidateRequestId(requestId);

            string fileName = $"{requestId}{UnityCliLoopConstants.JSON_FILE_EXTENSION}";
            return Path.Combine(CompileResultDirectoryPath, fileName);
        }

        private static void ValidateRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or empty");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("requestId must not be null or whitespace.", nameof(requestId));
            }

            if (!CompileRequestIdRules.IsSafe(requestId))
            {
                throw new ArgumentException(
                    "requestId may contain only ASCII letters, digits, underscore, or hyphen.",
                    nameof(requestId));
            }
        }

        private static void PublishResultFile(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            string inProgressTempFilePath = filePath + InProgressResultTempFileSuffix;
            string completedTempFilePath = filePath + CompletedResultTempFileSuffix;
            DeleteFileIfExists(inProgressTempFilePath);
            DeleteFileIfExists(completedTempFilePath);

            File.WriteAllText(inProgressTempFilePath, content, Encoding.UTF8);
            File.Move(inProgressTempFilePath, completedTempFilePath);

            if (File.Exists(filePath))
            {
                File.Replace(completedTempFilePath, filePath, null);
                return;
            }

            File.Move(completedTempFilePath, filePath);
        }

        private static void DeleteFileIfExists(string filePath)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(filePath), "filePath must not be null or empty");

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
