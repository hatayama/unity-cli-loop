using System.IO;
using UnityEditor;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Application service responsible for compilation lock file management.
    /// Single responsibility: Create/delete lock file during compilation for CLI detection.
    /// Related classes: DomainReloadDetectionService (similar pattern for domain reload)
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    [InitializeOnLoad]
    public static class CompilationLockService
    {
        private const string LOCK_FILE_NAME = "compiling.lock";

        private static string LockFilePath => Path.Combine(UnityEngine.Application.dataPath, "..", "Temp", LOCK_FILE_NAME);

        static CompilationLockService()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationStarted(object context)
        {
            CreateLockFile();
        }

        private static void OnCompilationFinished(object context)
        {
            DeleteLockFile();
        }

        private static void CreateLockFile()
        {
            string lockPath = LockFilePath;
            string tempDir = Path.GetDirectoryName(lockPath);

            if (!Directory.Exists(tempDir))
            {
                return;
            }

            File.WriteAllText(lockPath, System.DateTime.UtcNow.ToString("o"));
        }

        /// <summary>
        /// Delete lock file. Called on server startup to handle crash recovery.
        /// </summary>
        public static void DeleteLockFile()
        {
            string lockPath = LockFilePath;
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
    }
}
