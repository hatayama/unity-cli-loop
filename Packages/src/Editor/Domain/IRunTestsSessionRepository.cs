using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Persists pending run-tests recovery records and stored results across Domain Reload.
    /// </summary>
    public interface IRunTestsSessionRepository
    {
        void StorePendingRun(string requestId, DateTime expiresAtUtc);
        bool HasPendingRun(string requestId);
        IReadOnlyList<string> GetPendingRunRequestIds();
        bool HasAnyPendingRun();
        void ClearPendingRun(string requestId);
        void StoreRunResult(string requestId, string resultJson, DateTime completedAtUtc);
        UnityCliLoopStoredRunTestsResult GetRunResult(string requestId);
        void ClearExpired(DateTime utcNow);
    }
}
