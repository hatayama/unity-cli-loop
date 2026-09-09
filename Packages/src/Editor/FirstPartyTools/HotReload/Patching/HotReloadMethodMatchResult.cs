using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of resolving a hot-reload manifest entry to a MethodBase in the running AppDomain.
    /// </summary>
    internal sealed class HotReloadMethodMatchResult
    {
        public bool Success { get; }
        public MethodBase Method { get; }
        public HotReloadMethodMatchFailureReason FailureReason { get; }
        public string ErrorMessage { get; }
        public string Hint { get; }

        private HotReloadMethodMatchResult(
            bool success,
            MethodBase method,
            HotReloadMethodMatchFailureReason failureReason,
            string errorMessage,
            string hint)
        {
            Success = success;
            Method = method;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Hint = hint;
        }

        public static HotReloadMethodMatchResult SuccessResult(MethodBase method)
        {
            return new HotReloadMethodMatchResult(
                true, method, HotReloadMethodMatchFailureReason.None, string.Empty, string.Empty);
        }

        public static HotReloadMethodMatchResult Failure(
            HotReloadMethodMatchFailureReason reason, string errorMessage, string hint = "")
        {
            return new HotReloadMethodMatchResult(false, null, reason, errorMessage, hint);
        }
    }
}
