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
            HotReloadIntroducedTypeArtifact artifact,
            string errorMessage)
        {
            Success = success;
            Artifact = artifact;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }

        /// <summary>
        /// The artifact this run prepared, or null when the run introduced no type at all.
        /// </summary>
        public HotReloadIntroducedTypeArtifact Artifact { get; }

        public string ErrorMessage { get; }

        public static HotReloadIntroducedTypePreparationResult NoIntroducedTypes()
        {
            return new HotReloadIntroducedTypePreparationResult(true, null, string.Empty);
        }

        public static HotReloadIntroducedTypePreparationResult Prepared(
            HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            return new HotReloadIntroducedTypePreparationResult(true, artifact, string.Empty);
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
