namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How one exchange with one resident worker process ended: either the final result of the
    /// request, or the reason the conversation broke so the host may try once more.
    /// </summary>
    internal readonly struct TransformWorkerConversationOutcome
    {
        private TransformWorkerConversationOutcome(TransformWorkerHostResult result, string brokenReason)
        {
            Result = result;
            BrokenReason = brokenReason;
        }

        internal TransformWorkerHostResult Result { get; }
        internal string BrokenReason { get; }

        internal static TransformWorkerConversationOutcome Final(TransformWorkerHostResult result)
        {
            return new TransformWorkerConversationOutcome(result, null);
        }

        internal static TransformWorkerConversationOutcome Broken(string reason)
        {
            return new TransformWorkerConversationOutcome(null, reason);
        }
    }
}
