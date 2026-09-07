using System;

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
            string errorMessage)
        {
            Success = success;
            Prepared = prepared;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }

        /// <summary>
        /// What this run prepared, or null when the run introduced no type at all.
        /// </summary>
        public HotReloadPreparedIntroducedTypes Prepared { get; }

        public string ErrorMessage { get; }

        public static HotReloadIntroducedTypePreparationResult NoIntroducedTypes()
        {
            return new HotReloadIntroducedTypePreparationResult(true, null, string.Empty);
        }

        public static HotReloadIntroducedTypePreparationResult WithPrepared(
            HotReloadPreparedIntroducedTypes prepared)
        {
            if (prepared == null)
            {
                throw new ArgumentNullException(nameof(prepared));
            }

            return new HotReloadIntroducedTypePreparationResult(true, prepared, string.Empty);
        }

        public static HotReloadIntroducedTypePreparationResult Failure(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                throw new ArgumentException("A preparation failure must carry a reason.", nameof(errorMessage));
            }

            return new HotReloadIntroducedTypePreparationResult(false, null, errorMessage);
        }
    }
}
