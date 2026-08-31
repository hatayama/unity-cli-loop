using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Stores pending compile recovery requests for the current Unity Editor session.
    /// </summary>
    public interface IPendingCompileSessionRepository
    {
        void StorePendingCompileRequest(
            string requestId,
            bool forceRecompile,
            DateTime expiresAtUtc,
            bool reloadObserved);

        UnityCliLoopPendingCompileRequest[] GetPendingCompileRequests();
        bool MarkPendingCompileRequestReloadObserved();
        UnityCliLoopPendingCompileRequest GetPendingCompileRequestForRequestId(string requestId);
        void ClearPendingCompileRequest();
        bool ClearPendingCompileRequestIfMatches(string requestId);
        bool ClearExpiredPendingCompileRequest(DateTime utcNow);
    }
}
