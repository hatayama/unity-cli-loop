using System;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Invokes a compiled watch expression and converts user-code failures into history data.
    /// </summary>
    internal sealed class CompiledWatchExpressionEvaluator : IWatchExpressionEvaluator
    {
        private readonly Func<object> _evaluate;

        public CompiledWatchExpressionEvaluator(object instance, MethodInfo evaluateMethod)
        {
            _evaluate = (Func<object>)evaluateMethod.CreateDelegate(typeof(Func<object>), instance);
        }

        public WatchEvaluationResult Evaluate()
        {
            // Why: only the generated user-code delegate invocation may throw during normal watch evaluation;
            // converting that exception keeps the Editor update loop alive and preserves the error in history.
            try
            {
                object value = _evaluate();
                return WatchEvaluationResult.SuccessResult(value);
            }
            catch (Exception exception)
            {
                return WatchEvaluationResult.FailureResult(
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message);
            }
        }
    }
}
