namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Evaluates one already-compiled watch expression at the current Editor state.
    /// </summary>
    public interface IWatchExpressionEvaluator
    {
        WatchEvaluationResult Evaluate();
    }
}
