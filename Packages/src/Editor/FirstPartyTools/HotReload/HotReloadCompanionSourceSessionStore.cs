using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries a domain's companion sources across a Domain Reload through SessionState. Every
    /// member reads or writes SessionState, so each call has to come from the Unity main thread.
    /// </summary>
    /// <remarks>
    /// Why SessionState and not the domain alone: a Play-entry domain reload drops the in-memory
    /// ledger with the patches, and the reload that applies those patches again after Play entry
    /// needs the same companions. A successful compile clears it, because the compiled assembly
    /// then carries what the companions were brought in to bind against.
    /// </remarks>
    internal static class HotReloadCompanionSourceSessionStore
    {
        internal static void Save(HotReloadCompanionSourceLedger ledger)
        {
            SessionState.SetString(HotReloadConstants.CompanionSourcesSessionStateKey, ledger.Serialize());
        }

        internal static void Load(HotReloadCompanionSourceLedger ledger)
        {
            ledger.Restore(SessionState.GetString(HotReloadConstants.CompanionSourcesSessionStateKey, string.Empty));
        }

        internal static void Clear()
        {
            SessionState.EraseString(HotReloadConstants.CompanionSourcesSessionStateKey);
        }
    }
}
