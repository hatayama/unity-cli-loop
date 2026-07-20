using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns compile result shaping and SessionState persistence for delayed CLI polling.
    /// </summary>
    internal static class CompileResultSessionRecorder
    {
        internal static CompileResponse RecordCompileResult(
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository,
            string requestId,
            bool forceRecompile,
            CompileResult result,
            string correlationId,
            string pausePointWarning = null)
        {
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(result != null, "result must not be null");

            CompileResponse response = CompileResponseFactory.CreateResponse(result, forceRecompile, pausePointWarning);
            return RecordCompileResponse(
                compileResultSessionRepository,
                pendingCompileSessionRepository,
                requestId,
                forceRecompile,
                response,
                correlationId);
        }

        internal static CompileResponse RecordCompileResponse(
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository,
            string requestId,
            bool forceRecompile,
            CompileResponse response,
            string correlationId)
        {
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(response != null, "response must not be null");

            CompileSessionResultStore.StoreCompileResult(
                compileResultSessionRepository,
                pendingCompileSessionRepository,
                requestId,
                forceRecompile,
                response,
                correlationId);
            return response;
        }
    }
}
