namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One group plan's deferred classification: whether every input of that plan was
    /// short-circuited as already active.
    /// </summary>
    internal readonly struct HotReloadDeferredInputPlan
    {
        internal HotReloadDeferredInputPlan(int planIndex, bool isAllDeferred)
        {
            PlanIndex = planIndex;
            IsAllDeferred = isAllDeferred;
        }

        internal int PlanIndex { get; }
        internal bool IsAllDeferred { get; }
    }
}
