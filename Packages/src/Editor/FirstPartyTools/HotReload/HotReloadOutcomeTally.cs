namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How many method outcomes of a run fell into each kind.
    /// </summary>
    internal readonly struct HotReloadOutcomeTally
    {
        public int PatchedCount { get; }
        public int FailedCount { get; }
        public int SkippedCount { get; }
        public int AlreadyActiveCount { get; }
        public int AddedCount { get; }
        public int StaleCount { get; }

        public bool HasFailure => FailedCount > 0;

        public HotReloadOutcomeTally(
            int patchedCount,
            int failedCount,
            int skippedCount,
            int alreadyActiveCount,
            int addedCount,
            int staleCount)
        {
            PatchedCount = patchedCount;
            FailedCount = failedCount;
            SkippedCount = skippedCount;
            AlreadyActiveCount = alreadyActiveCount;
            AddedCount = addedCount;
            StaleCount = staleCount;
        }
    }
}
