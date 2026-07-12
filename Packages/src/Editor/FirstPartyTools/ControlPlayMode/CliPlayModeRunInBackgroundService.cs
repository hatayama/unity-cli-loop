#nullable enable
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies CLI PlayMode runInBackground overrides through Unity Application/Editor hooks.
    /// </summary>
    internal sealed class CliPlayModeRunInBackgroundService
    {
        private readonly CliPlayModeRunInBackgroundController _controller;
        private bool _isPlayModeCallbackRegistered;

        public CliPlayModeRunInBackgroundService(CliPlayModeRunInBackgroundController controller)
        {
            System.Diagnostics.Debug.Assert(controller != null, "controller must not be null");
            _controller = controller!;
        }

        /// <summary>
        /// Subscribes to PlayMode exit and re-applies or cleans up state after domain reload.
        /// </summary>
        public void InitializeForEditorStartup()
        {
            RegisterPlayModeCallback();

            bool? desiredRunInBackground = _controller.OnEditorStartup(EditorApplication.isPlaying);
            if (desiredRunInBackground.HasValue)
            {
                Application.runInBackground = desiredRunInBackground.Value;
            }
        }

        /// <summary>
        /// Enables runInBackground for a CLI-started PlayMode transition from EditMode.
        /// </summary>
        public void EnableForCliPlayStart()
        {
            bool desiredRunInBackground = _controller.OnCliPlayStarting(Application.runInBackground);
            Application.runInBackground = desiredRunInBackground;
        }

        private void RegisterPlayModeCallback()
        {
            if (_isPlayModeCallbackRegistered)
            {
                return;
            }

            // Why: domain reload re-runs startup; unsubscribe first to avoid duplicate handlers.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _isPlayModeCallbackRegistered = true;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Why: ExitingPlayMode covers both CLI Stop and the Editor toolbar Stop button.
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            bool? restoredRunInBackground = _controller.OnPlayModeExiting();
            if (restoredRunInBackground.HasValue)
            {
                Application.runInBackground = restoredRunInBackground.Value;
            }
        }
    }
}
