namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Which introduced-type diagnostics the Editor has to treat as a failure of the whole group
    /// rather than as a notice beside a run that continues.
    /// </summary>
    internal static class HotReloadIntroducedTypeFailureCodes
    {
        /// <summary>
        /// Whether the diagnostic refuses a type the run already retains an assembly for. Such a
        /// declaration is taken out of the tree either way, so continuing would bind callers
        /// against the retained definition the edited source no longer declares. Every other
        /// diagnostic names a declaration that was simply not introduced and whose source stays
        /// in the tree, so the run continues and only has to say what will keep not working.
        /// </summary>
        internal static bool IsRedefinedTypeFailure(HotReloadWorkerReasonCode code)
        {
            return code == HotReloadWorkerReasonCode.IntroducedTypeChanged
                || code == HotReloadWorkerReasonCode.IntroducedTypeMemberBodyChanged;
        }
    }
}
