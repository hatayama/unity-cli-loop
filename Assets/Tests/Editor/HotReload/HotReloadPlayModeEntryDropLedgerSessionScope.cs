using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Snapshots the live Editor Play-entry drop ledger so tests can restore it after
    /// mutating the production SessionState key.
    /// </summary>
    internal sealed class HotReloadPlayModeEntryDropLedgerSessionScope
    {
        private readonly string _capturedRaw;

        public HotReloadPlayModeEntryDropLedgerSessionScope()
        {
            _capturedRaw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                string.Empty);
            HotReloadPlayModeEntryDropLedger.Clear();
            HotReloadPlayModeEntryDropRecorder.ResetPendingForTesting();
        }

        public void Restore()
        {
            HotReloadPlayModeEntryDropRecorder.ResetPendingForTesting();
            SessionState.SetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                _capturedRaw);
        }
    }
}
