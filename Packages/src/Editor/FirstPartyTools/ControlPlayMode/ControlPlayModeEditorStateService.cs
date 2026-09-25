using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads and writes Unity's Play Mode state for the Control Play Mode use case.
    /// </summary>
    public interface IControlPlayModeEditorStateService
    {
        bool IsPlaying { get; set; }
        bool IsPaused { get; set; }

        // Null when the default Play Mode configuration is active or the Editor has no configurations.
        string ActiveScenarioName { get; }

        void Step();
    }

    /// <summary>
    /// Forwards Play Mode state reads/writes to the real EditorApplication API.
    /// </summary>
    internal sealed class ControlPlayModeEditorStateService : IControlPlayModeEditorStateService
    {
        private readonly IPlayModeScenarioBridge _scenarioBridge;

        internal ControlPlayModeEditorStateService(IPlayModeScenarioBridge scenarioBridge)
        {
            _scenarioBridge = scenarioBridge ?? throw new System.ArgumentNullException(nameof(scenarioBridge));
        }

        public bool IsPlaying
        {
            get => EditorApplication.isPlaying;
            set
            {
                if (!value)
                {
                    PlayModeStopReasonSessionStore.SetPending(
                        ControlPlayModeConstants.StoppedByCliControlPlayMode);
                }

                if (_scenarioBridge.IsNonDefaultScenarioActive)
                {
                    SetScenarioPlaying(value);
                    return;
                }

                EditorApplication.isPlaying = value;
            }
        }

        public string ActiveScenarioName => _scenarioBridge.ActiveScenarioName;

        public bool IsPaused
        {
            get => EditorApplication.isPaused;
            set => EditorApplication.isPaused = value;
        }

        public void Step()
        {
            EditorApplication.Step();
        }

        private void SetScenarioPlaying(bool value)
        {
            if (value)
            {
                // The Editor's Play button starts the active configuration through PlayModeManager.Start();
                // writing EditorApplication.isPlaying directly would skip the scenario and never launch Virtual Players.
                _scenarioBridge.Start();
                return;
            }

            // Mirror the Editor's Stop button: a Play Mode entered outside the scenario (for example by writing
            // EditorApplication.isPlaying directly) is not tracked by PlayModeManager, whose Stop() would then
            // be a no-op and leave the main Editor playing.
            if (!_scenarioBridge.IsScenarioRunning)
            {
                EditorApplication.isPlaying = false;
            }

            _scenarioBridge.Stop();
        }
    }
}
