using System;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Exits the Editor once a deadline has passed, and only once, so that quit-editor can answer before
    /// the process ends.
    /// </summary>
    internal sealed class DelayedEditorExit
    {
        private readonly double _exitAt;
        private readonly Action<int> _exit;
        private bool _hasExited;

        internal DelayedEditorExit(double exitAt, Action<int> exit)
        {
            _exitAt = exitAt;
            _exit = exit;
        }

        /// <summary>
        /// Exits with code 0 once now has reached the deadline. Returns true once the exit has been
        /// requested, so the caller can stop checking.
        /// </summary>
        internal bool ExitIfDue(double now)
        {
            if (_hasExited)
            {
                return true;
            }

            if (now < _exitAt)
            {
                return false;
            }

            _hasExited = true;
            _exit(0);
            return true;
        }
    }
}
