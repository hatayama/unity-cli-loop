using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes whether a compile controller should record its result for delayed CLI polling.
    /// </summary>
    internal readonly struct CompileResultRecordingContext
    {
        private CompileResultRecordingContext(bool enabled, string requestId, bool forceRecompile)
        {
            Enabled = enabled;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
        }

        internal bool Enabled { get; }
        internal string RequestId { get; }
        internal bool ForceRecompile { get; }

        internal static CompileResultRecordingContext Disabled()
        {
            return new CompileResultRecordingContext(false, "", false);
        }

        internal static CompileResultRecordingContext Create(CompileSchema request)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!CanRecord(request))
            {
                return Disabled();
            }

            return new CompileResultRecordingContext(
                true,
                request.RequestId,
                request.ForceRecompile);
        }

        internal static bool CanRecord(CompileSchema request)
        {
            Debug.Assert(request != null, "request must not be null");
            return request.WaitForDomainReload && !string.IsNullOrWhiteSpace(request.RequestId);
        }
    }
}
