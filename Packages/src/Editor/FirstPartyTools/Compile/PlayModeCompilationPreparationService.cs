using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Play Mode compilation preparation service
    /// Single function: Determine preparation action based on Play Mode state and settings
    /// Related classes: CompileUseCase, CompilationStateValidationService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public class PlayModeCompilationPreparationService
    {
        private const string EDITOR_PREFS_KEY = "ScriptCompilationDuringPlay";

        /// <summary>
        /// Determine preparation action based on Play Mode state and Unity settings
        /// </summary>
        /// <returns>Preparation result indicating what action to take before compilation</returns>
        public PreparationResult DeterminePreparationAction()
        {
            bool isPlaying = EditorApplication.isPlaying;

            if (!isPlaying)
            {
                return PreparationResult.Proceed();
            }

            int settingValue = EditorPrefs.GetInt(EDITOR_PREFS_KEY, 0);
            ScriptChangesDuringPlayOptions setting = (ScriptChangesDuringPlayOptions)settingValue;

            return setting switch
            {
                ScriptChangesDuringPlayOptions.RecompileAndContinuePlaying
                    => PreparationResult.Proceed(),
                ScriptChangesDuringPlayOptions.StopPlayingAndRecompile
                    => PreparationResult.NeedsStopPlayMode(),
                ScriptChangesDuringPlayOptions.RecompileAfterFinishedPlaying
                    => PreparationResult.CannotProceed(
                        "Cannot compile while in Play Mode. Unity's 'Script Changes While Playing' is set to " +
                        "'Recompile After Finished Playing'. Stop Play Mode manually or change the setting."),
                _ => PreparationResult.Proceed()
            };
        }

        /// <summary>
        /// Stop Play Mode to allow compilation
        /// </summary>
        public void StopPlayMode()
        {
            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }
            EditorApplication.isPlaying = false;
        }
    }

    /// <summary>
    /// Data model representing preparation result for compilation
    /// </summary>
    public class PreparationResult
    {
        /// <summary>
        /// Whether compilation can proceed
        /// </summary>
        public bool CanProceed { get; }

        /// <summary>
        /// Whether Play Mode needs to be stopped before compilation
        /// </summary>
        public bool NeedsPlayModeStop { get; }

        /// <summary>
        /// Error message when compilation cannot proceed
        /// </summary>
        public string ErrorMessage { get; }

        private PreparationResult(bool canProceed, bool needsStop, string error)
        {
            CanProceed = canProceed;
            NeedsPlayModeStop = needsStop;
            ErrorMessage = error;
        }

        /// <summary>
        /// Create result indicating compilation can proceed immediately
        /// </summary>
        public static PreparationResult Proceed() => new(true, false, null);

        /// <summary>
        /// Create result indicating Play Mode must be stopped first
        /// </summary>
        public static PreparationResult NeedsStopPlayMode() => new(true, true, null);

        /// <summary>
        /// Create result indicating compilation cannot proceed
        /// </summary>
        /// <param name="reason">Reason why compilation cannot proceed</param>
        public static PreparationResult CannotProceed(string reason) => new(false, false, reason);
    }
}
