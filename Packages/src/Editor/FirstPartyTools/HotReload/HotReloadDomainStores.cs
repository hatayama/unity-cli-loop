using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The domain-scoped stores a full revert empties, reset as one step.
    /// </summary>
    /// <remarks>
    /// Why one place: a revert-all has to leave the same state a Domain Reload would, so it must
    /// empty every store whose contents only make sense within the current domain. A new
    /// domain-scoped store belongs in this method; adding one and forgetting the revert path is
    /// the failure this exists to prevent.
    /// Why HotReloadIntroducedTypeRegistry is not here: an introduced type's identity is fixed
    /// until the next Domain Reload, so a revert cannot drop it (docs/hot-reload-introduced-types.md).
    /// Why HotReloadPlayModeEntryDropLedger is not here: it lives on SessionState, and
    /// HotReloadStatusExecutor.ExecuteRevertAll clears it through NotifyRevertAll.
    /// Static because the stores it fronts are static; it adds no state of its own.
    /// </remarks>
    internal static class HotReloadDomainStores
    {
        /// <summary>
        /// Empties every domain-scoped store. Paired with a full revert.
        /// </summary>
        internal static void ResetForRevertAll()
        {
            HotReloadFileGenerations.ClearAll();
            HotReloadAddedFieldStore.Clear();
            HotReloadAddedFieldRegistry.ClearAll();
            HotReloadInvocationRegistry.Clear();
            HotReloadAppliedSourceLedger.ClearAll();
            HotReloadSupersededSignatureRegistry.ClearAll();
        }
    }
}
