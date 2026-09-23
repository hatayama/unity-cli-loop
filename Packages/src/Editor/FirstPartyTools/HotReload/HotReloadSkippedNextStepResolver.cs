using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Chooses the next step of the skipped rows whose reason names a type this reload builds
    /// from source while compiled code still names the compiled copy. The worker words only the
    /// facts of those rows; which step works depends on how the files declaring the type entered
    /// the run and what the run recorded for them, which only the Editor knows once the run is
    /// written.
    /// </summary>
    internal sealed class HotReloadSkippedNextStepResolver
    {
        private readonly Func<string, HotReloadCarriedInState> _describeState;
        private readonly HashSet<string> _reappliedPaths;

        /// <param name="describeState">Where a project-relative file stands once the run is written.</param>
        /// <param name="reappliedPaths">Project-relative files this run brought back as siblings.</param>
        public HotReloadSkippedNextStepResolver(
            Func<string, HotReloadCarriedInState> describeState,
            IReadOnlyCollection<string> reappliedPaths)
        {
            if (describeState == null)
            {
                throw new ArgumentNullException(nameof(describeState));
            }

            if (reappliedPaths == null)
            {
                throw new ArgumentNullException(nameof(reappliedPaths));
            }

            _describeState = describeState;
            // Why the ledger's comparer: the paths are matched against files the ledger reports, and
            // a different case rule would split one file into two on Windows.
            _reappliedPaths = new HashSet<string>(
                reappliedPaths,
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
        }

        /// <summary>
        /// The outcomes in the same order, with every carried-in and compiled-signature row given
        /// its next step. Other rows are returned as they are.
        /// </summary>
        public List<HotReloadMethodOutcome> Resolve(IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            List<HotReloadMethodOutcome> resolved = new List<HotReloadMethodOutcome>(outcomes);
            // Why carried-in rows first: a compiled-signature row about the same type points to
            // the carried-in row's step, so that step has to be known before those rows are worded.
            Dictionary<string, LeftOutRow> leftOutByType = ResolveCarriedInRows(resolved);
            ResolveCompiledSignatureRows(resolved, leftOutByType);
            return resolved;
        }

        private Dictionary<string, LeftOutRow> ResolveCarriedInRows(List<HotReloadMethodOutcome> rows)
        {
            Dictionary<string, LeftOutRow> leftOutByType = new Dictionary<string, LeftOutRow>(StringComparer.Ordinal);
            for (int index = 0; index < rows.Count; index++)
            {
                HotReloadMethodOutcome row = rows[index];
                if (!IsCode(row, HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType))
                {
                    continue;
                }

                HotReloadWorkerReasonFacts facts = row.WorkerReason;
                CarriedInStep step = DecideCarriedInStep(facts);
                rows[index] = row.WithReason(row.Reason + " " + step.Text);
                string boundType = facts.Args[2];
                if (step.LeavesFileOut && !leftOutByType.ContainsKey(boundType))
                {
                    leftOutByType.Add(boundType, new LeftOutRow(row.Method, HotReloadSkippedNextStepText.Quote(facts.DeclaringFiles)));
                }
            }

            return leftOutByType;
        }

        private void ResolveCompiledSignatureRows(
            List<HotReloadMethodOutcome> rows,
            Dictionary<string, LeftOutRow> leftOutByType)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                HotReloadMethodOutcome row = rows[index];
                if (!IsCode(row, HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature))
                {
                    continue;
                }

                HotReloadWorkerReasonFacts facts = row.WorkerReason;
                string step = leftOutByType.TryGetValue(facts.Args[1], out LeftOutRow leftOut)
                    ? HotReloadSkippedNextStepText.SameFixAsCarriedInRow(leftOut.Method, leftOut.Files)
                    : DecideCompiledSignatureStep(facts);
                rows[index] = row.WithReason(row.Reason + " " + step);
            }
        }

        private string DecideCompiledSignatureStep(HotReloadWorkerReasonFacts facts)
        {
            // Why the sentence's own wording when no file was placed: the PDB places a type only
            // by a method body, so a type without one is described instead of named by path.
            if (facts.DeclaringFiles.Count == 0)
            {
                return HotReloadSkippedNextStepText.PassFiles(facts.Args[3]);
            }

            List<string> missing = new List<string>();
            foreach (string file in facts.DeclaringFiles)
            {
                if (_describeState(file) == HotReloadCarriedInState.NotInRun)
                {
                    missing.Add(file);
                }
            }

            return missing.Count == 0
                ? HotReloadSkippedNextStepText.AlreadyInReload(facts.DeclaringFiles)
                : HotReloadSkippedNextStepText.PassFiles(HotReloadSkippedNextStepText.Quote(missing));
        }

        private CarriedInStep DecideCarriedInStep(HotReloadWorkerReasonFacts facts)
        {
            // Why stop instead of falling back to the sentence's wording: the worker reports this
            // reason only when it found the source copy in this run's files, so an empty list means
            // the worker broke its contract and any step named here could be wrong.
            if (facts.DeclaringFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "A carried-in skip reason must name the files declaring " + facts.Args[2] + ".");
            }

            bool mustUndo = false;
            foreach (string file in facts.DeclaringFiles)
            {
                // Why the state before the re-applied set: a file that holds patches comes back as a
                // sibling too, and an undo does not stop it coming back, so it needs the same compile
                // as when it is passed.
                HotReloadCarriedInState state = _describeState(file);
                if (state == HotReloadCarriedInState.AppliedInThisRun
                    || state == HotReloadCarriedInState.ActiveFromEarlierRun)
                {
                    return new CarriedInStep(HotReloadSkippedNextStepText.CompileHoldingPatches("'" + file + "'"), false);
                }

                // Why a record of other bytes does not ask for an undo: it brings the file back once
                // the undo restores them.
                mustUndo |= _reappliedPaths.Contains(file)
                    || state == HotReloadCarriedInState.RecordedAtCurrentSource;
            }

            string files = HotReloadSkippedNextStepText.Quote(facts.DeclaringFiles);
            return new CarriedInStep(
                mustUndo
                    ? HotReloadSkippedNextStepText.UndoAndLeaveOut(files)
                    : HotReloadSkippedNextStepText.LeaveOut(files),
                true);
        }

        private static bool IsCode(HotReloadMethodOutcome row, HotReloadWorkerReasonCode code)
        {
            return row.WorkerReason != null && row.WorkerReason.Code == code;
        }

        private readonly struct CarriedInStep
        {
            public CarriedInStep(string text, bool leavesFileOut)
            {
                Text = text;
                LeavesFileOut = leavesFileOut;
            }

            public string Text { get; }

            public bool LeavesFileOut { get; }
        }

        private readonly struct LeftOutRow
        {
            public LeftOutRow(string method, string files)
            {
                Method = method;
                Files = files;
            }

            public string Method { get; }

            public string Files { get; }
        }
    }
}
