using System;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies compile result file persistence used by CLI domain-reload waiters.
    /// </summary>
    public sealed class CompileResultPersistenceServiceTests
    {
        [Test]
        public void SaveResult_WhenRequestIdContainsPathSeparator_ThrowsArgumentException()
        {
            // Verifies result persistence rejects request IDs that could escape the result directory.
            UnityCliLoopCompileResult result = CreateResult(success: true);

            Assert.That(
                () => CompileResultPersistenceService.SaveResult("../unsafe", result),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SaveResult_WhenTargetIsMissing_PublishesCompleteJsonWithoutSidecars()
        {
            // Verifies CLI pollers read a complete JSON file after result persistence publishes it.
            string requestId = CreateRequestId();
            string filePath = CreateResultFilePath(requestId);
            DeleteResultFileSet(filePath);

            try
            {
                CompileResultPersistenceService.SaveResult(requestId, CreateResult(success: true));

                string json = File.ReadAllText(filePath);
                UnityCliLoopCompileResult restored =
                    JsonConvert.DeserializeObject<UnityCliLoopCompileResult>(json);
                Assert.That(restored.Success, Is.True);
                Assert.That(File.Exists(filePath + ".tmp.write"), Is.False);
                Assert.That(File.Exists(filePath + ".tmp"), Is.False);
            }
            finally
            {
                DeleteResultFileSet(filePath);
            }
        }

        [Test]
        public void SaveResult_WhenTargetAlreadyExists_ReplacesPreviousJson()
        {
            // Verifies a repeated request ID publishes the newest complete compile result.
            string requestId = CreateRequestId();
            string filePath = CreateResultFilePath(requestId);
            DeleteResultFileSet(filePath);

            try
            {
                CompileResultPersistenceService.SaveResult(requestId, CreateResult(success: false));
                CompileResultPersistenceService.SaveResult(requestId, CreateResult(success: true));

                string json = File.ReadAllText(filePath);
                UnityCliLoopCompileResult restored =
                    JsonConvert.DeserializeObject<UnityCliLoopCompileResult>(json);
                Assert.That(restored.Success, Is.True);
            }
            finally
            {
                DeleteResultFileSet(filePath);
            }
        }

        private static UnityCliLoopCompileResult CreateResult(bool success)
        {
            return new UnityCliLoopCompileResult
            {
                Success = success,
                ErrorCount = success ? 0 : 1,
                WarningCount = 0,
                Errors = success ? Array.Empty<UnityCliLoopCompileIssue>() : new[]
                {
                    new UnityCliLoopCompileIssue
                    {
                        Message = "compile failed",
                        File = "",
                        Line = 0
                    }
                },
                Warnings = Array.Empty<UnityCliLoopCompileIssue>(),
                ProjectRoot = "<PROJECT_ROOT>"
            };
        }

        private static string CreateRequestId()
        {
            return "compile_persistence_test_" + Guid.NewGuid().ToString("N");
        }

        private static string CreateResultFilePath(string requestId)
        {
            return Path.Combine(CreateResultDirectoryPath(), requestId + UnityCliLoopConstants.JSON_FILE_EXTENSION);
        }

        private static string CreateResultDirectoryPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(
                projectRoot,
                UnityCliLoopConstants.TEMP_DIR,
                UnityCliLoopConstants.UNITYCLILOOP_DIR,
                UnityCliLoopConstants.COMPILE_RESULTS_DIR);
        }

        private static void DeleteResultFileSet(string filePath)
        {
            string[] paths =
            {
                filePath,
                filePath + ".tmp.write",
                filePath + ".tmp",
                filePath + ".bak"
            };
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
