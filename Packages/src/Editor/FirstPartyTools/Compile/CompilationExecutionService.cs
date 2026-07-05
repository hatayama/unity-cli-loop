using System;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compilation execution service
    /// Single function: Execute Unity project compilation
    /// Related classes: CompileController, CompileUseCase, CompileTool
    /// </summary>
    public class CompilationExecutionService
    {
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;

        public CompilationExecutionService(UnityCliLoopEditorSessionStateService sessionStateService)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            _sessionStateService =
                sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));
        }

        /// <summary>
        /// Execute compilation asynchronously
        /// </summary>
        /// <param name="request">Compile request with force and delayed-result settings.</param>
        /// <returns>Compilation result</returns>
        public async Task<CompileResult> ExecuteCompilationAsync(CompileSchema request, CancellationToken ct)
        {
            if (request == null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }

            using CompileController compileController = new(_sessionStateService);
            compileController.SetResultRecordingContext(CompileResultRecordingContext.Create(request));
            compileController.SetExternalSceneChangePolicy(request.ReloadExternalSceneChanges);
            return await compileController.TryCompileAsync(request.ForceRecompile, ct);
        }
    }
}
