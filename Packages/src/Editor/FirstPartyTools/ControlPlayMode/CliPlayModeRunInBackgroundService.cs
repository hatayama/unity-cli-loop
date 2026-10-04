#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns on the CLI runInBackground override for a Play Mode start requested by the CLI.
    /// </summary>
    public interface ICliPlayModeRunInBackgroundStarter
    {
        void EnableForCliPlayStart();
    }

    /// <summary>
    /// Applies CLI PlayMode runInBackground overrides through Unity Application/Editor hooks.
    /// </summary>
    internal sealed class CliPlayModeRunInBackgroundService : ICliPlayModeRunInBackgroundStarter
    {
        private readonly CliPlayModeRunInBackgroundController _controller;
        private readonly Action<bool> _setRunInBackground;
        private readonly Func<bool> _getRunInBackground;
        private bool _isPlayModeCallbackRegistered;

        public CliPlayModeRunInBackgroundService(
            CliPlayModeRunInBackgroundController controller,
            Action<bool>? setRunInBackground = null,
            Func<bool>? getRunInBackground = null)
        {
            System.Diagnostics.Debug.Assert(controller != null, "controller must not be null");
            _controller = controller!;
            _setRunInBackground = setRunInBackground ?? (value => Application.runInBackground = value);
            _getRunInBackground = getRunInBackground ?? (() => Application.runInBackground);
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
                _setRunInBackground(desiredRunInBackground.Value);
            }
        }

        /// <summary>
        /// Enables runInBackground for a CLI-started PlayMode transition from EditMode.
        /// </summary>
        public void EnableForCliPlayStart()
        {
            bool desiredRunInBackground = _controller.OnCliPlayStarting(_getRunInBackground());
            _setRunInBackground(desiredRunInBackground);
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

        internal void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Why: ExitingPlayMode covers CLI Stop and the toolbar Stop button, but Unity may
            // overwrite runInBackground during the transition or domain-reload afterward.
            // Peek+apply early, then commit clear on EnteredEditMode (or OnEditorStartup if reload).
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                bool? originalRunInBackground = _controller.PeekOriginalIfActive();
                if (originalRunInBackground.HasValue)
                {
                    _setRunInBackground(originalRunInBackground.Value);
                }

                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            bool? restoredRunInBackground = _controller.CommitRestoreAfterPlayModeExit();
            if (restoredRunInBackground.HasValue)
            {
                _setRunInBackground(restoredRunInBackground.Value);
            }
        }
    }
}
