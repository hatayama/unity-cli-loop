using System;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns CommandRunner single-flight state so Undo failures cannot permanently stick the slot.
    /// </summary>
    internal sealed class CommandRunnerExecutionSlot
    {
        private bool _isRunning;
        private CancellationTokenSource _cancellationTokenSource;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// Begins an execution slot after undo begin succeeds.
        /// Why undo before flipping running: Undo can throw during reload/startup; marking running
        /// first permanently rejects later requests as busy.
        /// </summary>
        public bool TryBegin(
            Func<int> beginUndoGroup,
            out int undoGroup,
            out CancellationTokenSource cancellationTokenSource)
        {
            System.Diagnostics.Debug.Assert(beginUndoGroup != null, "beginUndoGroup must not be null");

            undoGroup = -1;
            cancellationTokenSource = null;
            if (_isRunning)
            {
                return false;
            }

            undoGroup = beginUndoGroup();
            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource = _cancellationTokenSource;
            return true;
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Ends the execution slot even when undo collapse throws.
        /// Why nested finally: collapse can throw on a torn-down editor; the running flag must clear.
        /// </summary>
        public void End(int undoGroup, Action<int> collapseUndoGroup)
        {
            System.Diagnostics.Debug.Assert(collapseUndoGroup != null, "collapseUndoGroup must not be null");

            try
            {
                collapseUndoGroup(undoGroup);
            }
            finally
            {
                _isRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }
}
