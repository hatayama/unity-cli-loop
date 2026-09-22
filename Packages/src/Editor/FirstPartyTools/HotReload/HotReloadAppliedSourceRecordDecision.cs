using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How one processed file changes the domain's applied-source record at the end of a run.
    /// </summary>
    internal enum HotReloadAppliedSourceRecordKind
    {
        /// <summary>
        /// The run did not touch the file's live patch set, so the earlier record still
        /// describes it and is left as it is.
        /// </summary>
        Keep,

        /// <summary>Every row of the file applied, so its hash stands for what is loaded.</summary>
        FullyApplied,

        /// <summary>
        /// Part of the file was skipped or failed; the hash is kept only so an identical reload
        /// can explain why it re-applies.
        /// </summary>
        PartiallyApplied,

        /// <summary>
        /// The run changed what is live for the file, and no hash describes the result, so the
        /// earlier record is dropped.
        /// </summary>
        Forget
    }

    /// <summary>
    /// The applied-source record one processed file leaves behind, decided from that run's result
    /// alone so no record from an earlier run survives by accident.
    /// </summary>
    internal readonly struct HotReloadAppliedSourceRecordDecision
    {
        private HotReloadAppliedSourceRecordDecision(HotReloadAppliedSourceRecordKind kind, string hash)
        {
            Kind = kind;
            Hash = hash;
        }

        public HotReloadAppliedSourceRecordKind Kind { get; }

        /// <summary>The worker's hash of the compiled bytes; set only when a record is written.</summary>
        public string Hash { get; }

        // Why an empty hash keeps the record: the worker never read the file, so it touched no
        // patch, and the earlier record still describes the live patch set.
        // Why a file with no rows and no added fields or consts is forgotten: deleting an added
        // method and converging to compiled IL yields empty outcomes, and recording that as
        // non-baseline would make the next identical reload claim a Skipped/Failed that never
        // happened. Why a file with no rows but applied added fields or consts is recorded: that
        // file is still active, and a later reload that brings it back as a sibling needs this
        // hash to tell its bytes are the ones those members came from.
        public static HotReloadAppliedSourceRecordDecision Decide(
            string sourceContentSha256,
            IReadOnlyList<HotReloadMethodOutcome> outcomes,
            bool appliedAddedFieldsOrConsts)
        {
            Debug.Assert(outcomes != null, "outcomes must not be null.");

            if (string.IsNullOrEmpty(sourceContentSha256))
            {
                return new HotReloadAppliedSourceRecordDecision(HotReloadAppliedSourceRecordKind.Keep, null);
            }

            if (outcomes.Count == 0)
            {
                return appliedAddedFieldsOrConsts
                    ? new HotReloadAppliedSourceRecordDecision(
                        HotReloadAppliedSourceRecordKind.FullyApplied,
                        sourceContentSha256)
                    : new HotReloadAppliedSourceRecordDecision(HotReloadAppliedSourceRecordKind.Forget, null);
            }

            HotReloadAppliedSourceRecordKind kind = ClassifyRows(outcomes);
            return new HotReloadAppliedSourceRecordDecision(
                kind,
                kind == HotReloadAppliedSourceRecordKind.FullyApplied
                    || kind == HotReloadAppliedSourceRecordKind.PartiallyApplied
                    ? sourceContentSha256
                    : null);
        }

        // Why a Skipped or Failed row wins over every other row: the file then holds an edit that
        // is not loaded, whatever else applied. Why an AlreadyActive row keeps the record: it comes
        // only from the unchanged-source short-circuit, which matched that very record.
        // Why a Stale row forgets the record: the row keeps a patch for a method the source no
        // longer declares, and the file is not yet recorded as holding only what is loaded.
        private static HotReloadAppliedSourceRecordKind ClassifyRows(IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            bool hasUnapplied = false;
            bool hasAlreadyActive = false;
            bool hasStale = false;
            for (int index = 0; index < outcomes.Count; index++)
            {
                switch (outcomes[index].Kind)
                {
                    case HotReloadMethodOutcomeKind.Patched:
                    case HotReloadMethodOutcomeKind.Added:
                        break;
                    case HotReloadMethodOutcomeKind.Skipped:
                    case HotReloadMethodOutcomeKind.Failed:
                        hasUnapplied = true;
                        break;
                    case HotReloadMethodOutcomeKind.AlreadyActive:
                        hasAlreadyActive = true;
                        break;
                    case HotReloadMethodOutcomeKind.Stale:
                        hasStale = true;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unhandled hot reload outcome kind: " + outcomes[index].Kind);
                }
            }

            if (hasUnapplied)
            {
                return HotReloadAppliedSourceRecordKind.PartiallyApplied;
            }

            if (hasAlreadyActive)
            {
                return HotReloadAppliedSourceRecordKind.Keep;
            }

            return hasStale
                ? HotReloadAppliedSourceRecordKind.Forget
                : HotReloadAppliedSourceRecordKind.FullyApplied;
        }
    }
}
