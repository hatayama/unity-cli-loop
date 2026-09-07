using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports what one run's introduced-type preparation produced, so the group pipeline can tell
    /// "nothing to introduce" from a failure that must leave the run unapplied.
    /// </summary>
    internal sealed class HotReloadIntroducedTypePreparationResult
    {
        private HotReloadIntroducedTypePreparationResult(
            bool success,
            HotReloadPreparedIntroducedTypes prepared,
            string errorMessage,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> failures,
            IReadOnlyList<HotReloadIntroducedTypeNotice> notices)
        {
            Success = success;
            Prepared = prepared;
            ErrorMessage = errorMessage;
            AlreadyActiveTypes = alreadyActiveTypes ?? Array.Empty<HotReloadIntroducedTypeOutcome>();
            Failures = failures ?? Array.Empty<HotReloadIntroducedTypeOutcome>();
            Notices = notices ?? Array.Empty<HotReloadIntroducedTypeNotice>();
        }

        public bool Success { get; }

        /// <summary>
        /// What this run prepared, or null when the run introduced no type at all.
        /// </summary>
        public HotReloadPreparedIntroducedTypes Prepared { get; }

        /// <summary>The run-level reason a <see cref="WorkerFailure"/> carries; otherwise empty.</summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// The declarations this run refused, one row per owner. Empty for a run-level failure,
        /// which refused no declaration of its own.
        /// </summary>
        public IReadOnlyList<HotReloadIntroducedTypeOutcome> Failures { get; }

        /// <summary>
        /// What the preparation observed without refusing the run, carried even when the run
        /// introduced nothing: a declaration this stage does not introduce is exactly such a run.
        /// </summary>
        public IReadOnlyList<HotReloadIntroducedTypeNotice> Notices { get; }

        /// <summary>
        /// The declarations this run bound from an artifact the domain already retains. Reported
        /// even when the run introduced nothing, because binding a retained type is a run with no
        /// type of its own to prepare.
        /// </summary>
        public IReadOnlyList<HotReloadIntroducedTypeOutcome> AlreadyActiveTypes { get; }

        public static HotReloadIntroducedTypePreparationResult NoIntroducedTypes(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes = null,
            IReadOnlyList<HotReloadIntroducedTypeNotice> notices = null)
        {
            return new HotReloadIntroducedTypePreparationResult(
                true, null, string.Empty, alreadyActiveTypes, null, notices);
        }

        public static HotReloadIntroducedTypePreparationResult WithPrepared(
            HotReloadPreparedIntroducedTypes prepared,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes = null,
            IReadOnlyList<HotReloadIntroducedTypeNotice> notices = null)
        {
            if (prepared == null)
            {
                throw new ArgumentNullException(nameof(prepared));
            }

            return new HotReloadIntroducedTypePreparationResult(
                true, prepared, string.Empty, alreadyActiveTypes, null, notices);
        }

        /// <summary>
        /// The preparation itself could not run. Why not a type outcome: the preparation runs for
        /// every reload, including the ones that declare no type, so a reload that only edited a
        /// method body would otherwise be told to look at a list of types for its reason.
        /// </summary>
        public static HotReloadIntroducedTypePreparationResult WorkerFailure(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                throw new ArgumentException("A preparation failure must carry a reason.", nameof(errorMessage));
            }

            return new HotReloadIntroducedTypePreparationResult(
                false, null, errorMessage, null, null, null);
        }

        /// <summary>
        /// The preparation ran and refused one or more declarations. Overloads are forbidden, so
        /// the two failure kinds are separate names rather than one name with two shapes.
        /// </summary>
        /// <remarks>
        /// Why a refusal still carries the other findings: one preparation covers every
        /// declaration of the group, so the reuses and notices it collected describe declarations
        /// the refusal says nothing about, and dropping them would hide them until a compile.
        /// </remarks>
        public static HotReloadIntroducedTypePreparationResult TypeFailures(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> failures,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes = null,
            IReadOnlyList<HotReloadIntroducedTypeNotice> notices = null)
        {
            if (failures == null || failures.Count == 0)
            {
                throw new ArgumentException("A type failure must carry a refused declaration.", nameof(failures));
            }

            return new HotReloadIntroducedTypePreparationResult(
                false, null, string.Empty, alreadyActiveTypes, failures, notices);
        }
    }
}
