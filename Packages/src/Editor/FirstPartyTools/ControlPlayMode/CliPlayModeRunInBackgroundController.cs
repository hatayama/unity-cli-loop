#nullable enable
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores whether a CLI-started PlayMode session owns a temporary Application.runInBackground override.
    /// </summary>
    internal interface ICliPlayModeRunInBackgroundStore
    {
        bool IsActive { get; }

        bool OriginalRunInBackground { get; }

        void Activate(bool originalRunInBackground);

        void Clear();
    }

    /// <summary>
    /// Pure state machine for keeping runInBackground true during CLI-initiated PlayMode.
    /// Why: Editor throttles the player loop when unfocused unless runInBackground is true, which
    /// breaks time-dependent E2E between CLI commands. Manual Play must not be affected.
    /// </summary>
    internal sealed class CliPlayModeRunInBackgroundController
    {
        private readonly ICliPlayModeRunInBackgroundStore _store;

        public CliPlayModeRunInBackgroundController(ICliPlayModeRunInBackgroundStore store)
        {
            Debug.Assert(store != null, "store must not be null");
            _store = store!;
        }

        /// <summary>
        /// Records the pre-Play value and requests runInBackground=true for a CLI Play start.
        /// Returns the value that Application.runInBackground should be set to.
        /// </summary>
        public bool OnCliPlayStarting(bool currentRunInBackground)
        {
            if (!_store.IsActive)
            {
                _store.Activate(currentRunInBackground);
            }

            return true;
        }

        /// <summary>
        /// Restores the original runInBackground value when PlayMode ends (CLI Stop or manual).
        /// Returns null when this controller did not own an override.
        /// </summary>
        public bool? OnPlayModeExiting()
        {
            if (!_store.IsActive)
            {
                return null;
            }

            bool originalRunInBackground = _store.OriginalRunInBackground;
            _store.Clear();
            return originalRunInBackground;
        }

        /// <summary>
        /// Recovers override state after domain reload.
        /// Why: SessionState survives domain reload but Application.runInBackground may not stay true,
        /// and an orphaned active flag after Play has already ended must not stick forever.
        /// Returns the value to apply, or null when no write is needed.
        /// </summary>
        public bool? OnEditorStartup(bool isPlaying)
        {
            if (!_store.IsActive)
            {
                return null;
            }

            if (isPlaying)
            {
                return true;
            }

            bool originalRunInBackground = _store.OriginalRunInBackground;
            _store.Clear();
            return originalRunInBackground;
        }
    }
}
