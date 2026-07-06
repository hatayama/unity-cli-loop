using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Persists compile results that must survive Unity Editor Domain Reload within the current session.
    /// </summary>
    public interface ICompileResultSessionRepository
    {
        void StoreCompileResult(
            string requestId,
            bool forceRecompile,
            string resultJson,
            DateTime completedAtUtc);

        UnityCliLoopStoredCompileResult GetCompileResult(string requestId);
        UnityCliLoopStoredCompileResult GetStoredCompileResult();
        UnityCliLoopStoredCompileResult[] GetStoredCompileResults();
        void ClearCompileResult();
        bool ClearExpiredCompileResult(DateTime utcNow, TimeSpan lifetime);
    }
}
