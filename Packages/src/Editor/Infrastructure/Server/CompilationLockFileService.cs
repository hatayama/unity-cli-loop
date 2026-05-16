using System.IO;
using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Application service responsible for compilation lock file management.
    /// Single responsibility: Create/delete lock file during compilation for CLI detection.
    /// Related classes: DomainReloadDetectionService (similar pattern for domain reload)
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public sealed class CompilationLockFileService : ICompilationLockService
    {
        private const string LOCK_FILE_NAME = "compiling.lock";
        private readonly ServerReadinessStateStore _stateStore;

        internal CompilationLockFileService(ServerReadinessStateStore stateStore = null)
        {
            _stateStore = stateStore ?? new ServerReadinessStateStore(UnityCliLoopPathResolver.GetProjectRoot());
        }

        private static string LockFilePath => Path.Combine(UnityEngine.Application.dataPath, "..", "Temp", LOCK_FILE_NAME);

        public void RegisterForEditorStartup()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private void OnCompilationStarted(object context)
        {
            MarkCompilationStarted();
        }

        private void OnCompilationFinished(object context)
        {
            MarkCompilationFinished();
        }

        internal void MarkCompilationStarted()
        {
            CreateLockFile();
            _stateStore.Write(
                ServerReadinessPhase.Compiling,
                ServerReadinessStateStore.CreateGenerationId(),
                "compilation-started",
                null,
                null);
        }

        internal void MarkCompilationFinished()
        {
            DeleteLockFileCore();
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
        public void DeleteLockFile()
        {
            DeleteLockFileCore();
        }

        private static void DeleteLockFileCore()
        {
            string lockPath = LockFilePath;
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
    }
}
