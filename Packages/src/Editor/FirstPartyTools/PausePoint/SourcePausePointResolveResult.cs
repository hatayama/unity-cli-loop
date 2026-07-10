using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of a file:line resolve attempt. Never thrown; failures are carried as data
    /// so callers can report a specific reason instead of an exception.
    /// </summary>
    internal sealed class SourcePausePointResolveResult
    {
        public bool Success { get; }
        public SourcePausePointResolveFailureReason FailureReason { get; }
        public string ErrorMessage { get; }
        public SourcePausePointResolution Resolution { get; }

        private SourcePausePointResolveResult(
            bool success,
            SourcePausePointResolveFailureReason failureReason,
            string errorMessage,
            SourcePausePointResolution resolution)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Resolution = resolution;
        }

        public static SourcePausePointResolveResult SuccessResult(SourcePausePointResolution resolution)
        {
            Debug.Assert(resolution != null, "resolution must not be null for a success result.");
            return new SourcePausePointResolveResult(true, SourcePausePointResolveFailureReason.None, string.Empty, resolution);
        }

        public static SourcePausePointResolveResult Failure(SourcePausePointResolveFailureReason reason, string errorMessage)
        {
            Debug.Assert(reason != SourcePausePointResolveFailureReason.None, "Failure requires a specific reason.");
            return new SourcePausePointResolveResult(false, reason, errorMessage, null);
        }
    }
}
