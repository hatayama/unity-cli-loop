using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Snapshots the live Editor Play-entry drop ledgers (identities and introduced owner files)
    /// so tests can restore them after mutating the production SessionState keys.
    /// </summary>
    internal sealed class HotReloadPlayModeEntryDropLedgerSessionScope
    {
        private readonly string _capturedRaw;

        private readonly string _capturedSourcesRaw;

        public HotReloadPlayModeEntryDropLedgerSessionScope()
        {
            _capturedRaw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                string.Empty);
            _capturedSourcesRaw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSourcesSessionStateKey,
                string.Empty);
            HotReloadPlayModeEntryDropLedger.Clear();
            HotReloadPlayModeEntryDropSourceLedger.Clear();
            HotReloadPlayModeEntryDropRecorder.ResetPendingForTesting();
        }

        public void Restore()
        {
            HotReloadPlayModeEntryDropRecorder.ResetPendingForTesting();
            SessionState.SetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                _capturedRaw);
            SessionState.SetString(
                HotReloadConstants.PlayModeEntryDropSourcesSessionStateKey,
                _capturedSourcesRaw);
        }
    }
}
