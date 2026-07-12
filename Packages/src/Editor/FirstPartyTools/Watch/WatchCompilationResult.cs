using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports compilation success or diagnostics for one watch expression.
    /// </summary>
    public sealed class WatchCompilationResult
    {
        private WatchCompilationResult(
            bool success,
            IWatchExpressionEvaluator evaluator,
            string errorMessage,
            IReadOnlyList<CompilationError> compilationErrors)
        {
            Success = success;
            Evaluator = evaluator;
            ErrorMessage = errorMessage;
            CompilationErrors = compilationErrors;
        }

        public bool Success { get; }
        public IWatchExpressionEvaluator Evaluator { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<CompilationError> CompilationErrors { get; }

        public static WatchCompilationResult SuccessResult(IWatchExpressionEvaluator evaluator)
        {
            return new WatchCompilationResult(true, evaluator, string.Empty, new List<CompilationError>());
        }

        public static WatchCompilationResult FailureResult(
            string errorMessage,
            IReadOnlyList<CompilationError> compilationErrors)
        {
            return new WatchCompilationResult(false, null, errorMessage, compilationErrors);
        }
    }
}
