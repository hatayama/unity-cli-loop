using System;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Invokes a compiled watch expression and converts user-code failures into history data.
    /// </summary>
    internal sealed class CompiledWatchExpressionEvaluator : IWatchExpressionEvaluator
    {
        private readonly object _instance;
        private readonly MethodInfo _evaluateMethod;

        public CompiledWatchExpressionEvaluator(object instance, MethodInfo evaluateMethod)
        {
            _instance = instance;
            _evaluateMethod = evaluateMethod;
        }

        public WatchEvaluationResult Evaluate()
        {
            // Why: only the generated user-code Invoke may throw during normal watch evaluation;
            // converting that exception keeps the Editor update loop alive and preserves the error in history.
            try
            {
                object value = _evaluateMethod.Invoke(_instance, null);
                return WatchEvaluationResult.SuccessResult(value);
            }
            catch (Exception exception)
            {
                Exception userException = exception is TargetInvocationException
                    && exception.InnerException != null
                    ? exception.InnerException
                    : exception;
                return WatchEvaluationResult.FailureResult(
                    userException.GetType().FullName ?? userException.GetType().Name,
                    userException.Message);
            }
        }
    }
}
