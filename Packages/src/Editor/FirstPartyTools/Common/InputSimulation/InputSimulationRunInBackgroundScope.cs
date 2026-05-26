#nullable enable
using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Temporarily keeps PlayMode running while input simulation tools wait for runtime updates.
    /// </summary>
    internal sealed class InputSimulationRunInBackgroundScope : IDisposable
    {
        private readonly bool _originalRunInBackground;
        private bool _isDisposed;

        private InputSimulationRunInBackgroundScope(bool originalRunInBackground)
        {
            _originalRunInBackground = originalRunInBackground;
        }

        public static InputSimulationRunInBackgroundScope Enable()
        {
            bool originalRunInBackground = UnityEngine.Application.runInBackground;
            if (!originalRunInBackground)
            {
                UnityEngine.Application.runInBackground = true;
            }

            return new InputSimulationRunInBackgroundScope(originalRunInBackground);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            UnityEngine.Application.runInBackground = _originalRunInBackground;
            _isDisposed = true;
        }
    }
}
