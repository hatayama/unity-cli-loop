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
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes)
        {
            Success = success;
            Prepared = prepared;
            ErrorMessage = errorMessage;
            AlreadyActiveTypes = alreadyActiveTypes ?? Array.Empty<HotReloadIntroducedTypeOutcome>();
        }

        public bool Success { get; }

        /// <summary>
        /// What this run prepared, or null when the run introduced no type at all.
        /// </summary>
        public HotReloadPreparedIntroducedTypes Prepared { get; }

        public string ErrorMessage { get; }

        /// <summary>
        /// The declarations this run bound from an artifact the domain already retains. Reported
        /// even when the run introduced nothing, because binding a retained type is a run with no
        /// type of its own to prepare.
        /// </summary>
        public IReadOnlyList<HotReloadIntroducedTypeOutcome> AlreadyActiveTypes { get; }

        public static HotReloadIntroducedTypePreparationResult NoIntroducedTypes(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes = null)
        {
            return new HotReloadIntroducedTypePreparationResult(true, null, string.Empty, alreadyActiveTypes);
        }

        public static HotReloadIntroducedTypePreparationResult WithPrepared(
            HotReloadPreparedIntroducedTypes prepared,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> alreadyActiveTypes = null)
        {
            if (prepared == null)
            {
                throw new ArgumentNullException(nameof(prepared));
            }

            return new HotReloadIntroducedTypePreparationResult(
                true, prepared, string.Empty, alreadyActiveTypes);
        }

        public static HotReloadIntroducedTypePreparationResult Failure(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                throw new ArgumentException("A preparation failure must carry a reason.", nameof(errorMessage));
            }

            return new HotReloadIntroducedTypePreparationResult(false, null, errorMessage, null);
        }
    }
}
