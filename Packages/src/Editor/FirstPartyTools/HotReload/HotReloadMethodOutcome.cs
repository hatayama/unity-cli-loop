using System;

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

        // The added member an AlreadyActive row left in place, whose own counter is the row's
        // InvocationCount. Null for every other row.
        public HotReloadAddedMemberInfo AddedMember { get; }

        // What a Failed row's failure was, which chooses the response's next step. None for every
        // other row.
        public HotReloadFailureKinds FailureKinds { get; }

        private HotReloadMethodOutcome(
            HotReloadMethodOutcomeKind kind,
            string method,
            string reason,
            string filePath,
            string lifecycleNote,
            HotReloadWorkerReasonFacts workerReason = null,
            HotReloadAddedMemberInfo addedMember = null,
            HotReloadFailureKinds failureKinds = HotReloadFailureKinds.None)
        {
            Kind = kind;
            Method = method;
            Reason = reason;
            FilePath = filePath;
            LifecycleNote = lifecycleNote ?? string.Empty;
            WorkerReason = workerReason;
            AddedMember = addedMember;
            FailureKinds = failureKinds;
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
                string.Empty,
                failureKinds: HotReloadFailureKinds.Declaration);
        }

        // A failure whose kinds were decided where it happened, such as an Editor that started
        // compiling during the run, which the next step must not report as something to fix.
        public static HotReloadMethodOutcome FailedBecause(
            string method,
            HotReloadFailureDescription failure,
            string filePath)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Failed,
                method,
                failure.Message,
                filePath,
                string.Empty,
                failureKinds: failure.Kinds);
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

        // An added member left in place because its file's source is unchanged. Why the member and
        // not its count: the ledger never counts an added member, and the response reads the
        // member's own counter when it is built, as it reads the ledger for a patch's row.
        public static HotReloadMethodOutcome AlreadyActiveAddedMember(
            HotReloadAddedMemberInfo member,
            string filePath)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.AlreadyActive,
                member.MethodKey,
                HotReloadConstants.AlreadyActiveAddedMemberReason,
                filePath,
                string.Empty,
                addedMember: member);
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
            return new HotReloadMethodOutcome(
                Kind,
                Method,
                Reason,
                FilePath,
                lifecycleNote,
                WorkerReason,
                AddedMember,
                FailureKinds);
        }

        public HotReloadMethodOutcome WithReason(string reason)
        {
            return new HotReloadMethodOutcome(
                Kind,
                Method,
                reason,
                FilePath,
                LifecycleNote,
                WorkerReason,
                AddedMember,
                FailureKinds);
        }

        public HotReloadMethodOutcome WithWorkerReason(HotReloadWorkerReasonFacts workerReason)
        {
            return new HotReloadMethodOutcome(
                Kind,
                Method,
                Reason,
                FilePath,
                LifecycleNote,
                workerReason,
                AddedMember,
                FailureKinds);
        }
    }
}
