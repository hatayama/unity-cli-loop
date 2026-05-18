using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Application service responsible for publishing compilation readiness state.
    /// Single responsibility: Mark the external readiness state while Unity is compiling.
    /// Related classes: DomainReloadDetectionService (similar readiness state publishing)
    /// </summary>
    public sealed class CompilationReadinessStatePublisher : ICompilationReadinessService
    {
        private readonly ServerReadinessStateStore _stateStore;
        private ServerReadinessState _stateBeforeCompilation;
        private string _activeCompilationGenerationId;

        internal CompilationReadinessStatePublisher(ServerReadinessStateStore stateStore = null)
        {
            _stateStore = stateStore ?? new ServerReadinessStateStore(UnityCliLoopPathResolver.GetProjectRoot());
        }

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
            string generationId = _activeCompilationGenerationId;
            ServerReadinessState stateBeforeCompilation = _stateBeforeCompilation;
            EditorApplication.delayCall += () =>
                RestoreStateBeforeCompilationIfStillCurrent(generationId, stateBeforeCompilation);
        }

        internal void MarkCompilationStarted()
        {
            _stateBeforeCompilation = _stateStore.Read();
            _activeCompilationGenerationId = ServerReadinessStateStore.CreateGenerationId();
            _stateStore.Write(
                ServerReadinessPhase.Compiling,
                _activeCompilationGenerationId,
                "compilation-started",
                null,
                null);
        }

        internal void MarkCompilationFinished()
        {
            RestoreStateBeforeCompilationIfStillCurrent(
                _activeCompilationGenerationId,
                _stateBeforeCompilation);
        }

        private void RestoreStateBeforeCompilationIfStillCurrent(
            string compilationGenerationId,
            ServerReadinessState stateBeforeCompilation)
        {
            if (string.IsNullOrWhiteSpace(compilationGenerationId))
            {
                return;
            }

            ServerReadinessState currentState = _stateStore.Read();
            if (currentState == null ||
                currentState.GenerationId != compilationGenerationId ||
                currentState.Phase != "compiling")
            {
                return;
            }

            if (stateBeforeCompilation == null)
            {
                _stateStore.Delete();
                return;
            }

            _stateStore.Write(stateBeforeCompilation);
        }
    }
}
