using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Snapshots the live Editor Play-entry drop ledgers (identities and introduced owner files)
    /// and the companion sources so tests can restore them after mutating the production
    /// SessionState keys.
    /// </summary>
    internal sealed class HotReloadPlayModeEntryDropLedgerSessionScope
    {
        private readonly string _capturedRaw;

        private readonly string _capturedSourcesRaw;

        private readonly string _capturedCompanionsRaw;

        private readonly string _capturedRewireFieldsRaw;

        public HotReloadPlayModeEntryDropLedgerSessionScope()
        {
            _capturedRaw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                string.Empty);
            _capturedSourcesRaw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSourcesSessionStateKey,
                string.Empty);
            _capturedCompanionsRaw = SessionState.GetString(
                HotReloadConstants.CompanionSourcesSessionStateKey,
                string.Empty);
            _capturedRewireFieldsRaw = SessionState.GetString(
                HotReloadConstants.RewireFieldsSessionStateKey,
                string.Empty);
            HotReloadPlayModeEntryDropLedger.Clear();
            HotReloadRewireLedger.Clear();
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
            SessionState.SetString(
                HotReloadConstants.CompanionSourcesSessionStateKey,
                _capturedCompanionsRaw);
            SessionState.SetString(
                HotReloadConstants.RewireFieldsSessionStateKey,
                _capturedRewireFieldsRaw);
        }
    }
}
