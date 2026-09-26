namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Per-method outcome: Patched, Skipped, Failed, Added, AlreadyActive, or Stale.
    /// </summary>
    internal sealed class HotReloadMethodOutcome
    {
        public HotReloadMethodOutcomeKind Kind { get; }
        public string Method { get; }
        public string Reason { get; }
        public string FilePath { get; }
        public string LifecycleNote { get; }

        // The facts Reason was built from when a worker reported it. Null for every other row.
        public HotReloadWorkerReasonFacts WorkerReason { get; }

        private HotReloadMethodOutcome(
            HotReloadMethodOutcomeKind kind,
            string method,
            string reason,
            string filePath,
            string lifecycleNote,
            HotReloadWorkerReasonFacts workerReason = null)
        {
            Kind = kind;
            Method = method;
            Reason = reason;
            FilePath = filePath;
            LifecycleNote = lifecycleNote ?? string.Empty;
            WorkerReason = workerReason;
        }

        public static HotReloadMethodOutcome Patched(
            string method,
            string filePath,
            string lifecycleNote = null)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Patched,
                method,
                string.Empty,
                filePath,
                lifecycleNote);
        }

        public static HotReloadMethodOutcome Skipped(string method, string reason, string filePath)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Skipped,
                method,
                reason,
                filePath,
                string.Empty);
        }

        public static HotReloadMethodOutcome Failed(string method, string reason, string filePath)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Failed,
                method,
                reason,
                filePath,
                string.Empty);
        }

        public static HotReloadMethodOutcome Added(
            string method,
            string filePath,
            string lifecycleNote = null)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Added,
                method,
                string.Empty,
                filePath,
                lifecycleNote);
        }

        public static HotReloadMethodOutcome AlreadyActive(
            string method,
            string filePath,
            string reason = null)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.AlreadyActive,
                method,
                string.IsNullOrEmpty(reason)
                    ? HotReloadConstants.AlreadyActiveReason
                    : reason,
                filePath,
                string.Empty);
        }

        // A patch that outlived the method it replaced: the edited source no longer declares it,
        // so compiled callers keep running the patched body until 'uloop compile', '--revert-all',
        // or a later reload whose source restores the method to the compiled baseline reverts it.
        // A reload that declares the method with a different body only replaces the patch.
        public static HotReloadMethodOutcome Stale(string method, string filePath)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Stale,
                method,
                HotReloadConstants.StalePatchRemovedFromSourceReason,
                filePath,
                string.Empty);
        }

        public HotReloadMethodOutcome WithLifecycleNote(string lifecycleNote)
        {
            return new HotReloadMethodOutcome(Kind, Method, Reason, FilePath, lifecycleNote, WorkerReason);
        }

        public HotReloadMethodOutcome WithReason(string reason)
        {
            return new HotReloadMethodOutcome(Kind, Method, reason, FilePath, LifecycleNote, WorkerReason);
        }

        public HotReloadMethodOutcome WithWorkerReason(HotReloadWorkerReasonFacts workerReason)
        {
            return new HotReloadMethodOutcome(Kind, Method, Reason, FilePath, LifecycleNote, workerReason);
        }
    }
}
