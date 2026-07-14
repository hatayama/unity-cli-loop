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
        private readonly ICompileResultSessionRepository _compileResultSessionRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;

        public CompilationExecutionService(
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

            _compileResultSessionRepository = compileResultSessionRepository ??
                throw new ArgumentNullException(nameof(compileResultSessionRepository));
            _pendingCompileSessionRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
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

            using CompileController compileController = new(
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);
            compileController.SetResultRecordingContext(CompileResultRecordingContext.Create(request));
            compileController.SetExternalSceneChangePolicy(request.ReloadExternalSceneChanges);
            return await compileController.TryCompileAsync(request.ForceRecompile, ct).ConfigureAwait(false);
        }
    }
}
