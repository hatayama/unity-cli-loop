namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What an apply run asks the CLI to do about the edits it could not apply.
    /// </summary>
    internal enum HotReloadCompileFallbackDecision
    {
        NotNeeded = 0,
        Requested = 1,
        HeldForPlayMode = 2,
        Disabled = 3,
        BlockedByPlayModeSetting = 4
    }

    /// <summary>
    /// Decides whether a hot-reload apply run falls back to a compile in the same command.
    /// </summary>
    internal static class HotReloadCompileFallbackDecider
    {
        /// <summary>
        /// True when the run left at least one requested edit unapplied, whatever the reason: a
        /// method it could not patch and a declaration it refused both leave the edited source
        /// reachable only through a compile. Skipped rows of a sibling the run pulled in to re-apply
        /// earlier changes do not count: they are not this run's edits, and their earlier patches
        /// stay active. Its Failed rows do count: a failed run reverts the sibling's earlier
        /// patches, so only a compile brings them back.
        /// </summary>
        /// <remarks>
        /// A sibling that came back for another reason (a retry after an earlier Skip, or a
        /// companion) is not in activePatchSiblingFiles, so its rows still count: a retried row is
        /// an edit that was never applied.
        /// A declaration a sibling owns is left out even when refused: an introduced type is never
        /// unloaded, so a failed run does not take the sibling's earlier declaration away.
        /// </remarks>
        internal static bool HasUnappliedEdit(
            HotReloadOrchestratorResult result,
            HotReloadReappliedSiblingFiles activePatchSiblingFiles)
        {
            foreach (HotReloadMethodOutcome method in result.Methods)
            {
                if (method.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    return true;
                }

                if (method.Kind == HotReloadMethodOutcomeKind.Skipped
                    && !activePatchSiblingFiles.Contains(method.FilePath))
                {
                    return true;
                }
            }

            foreach (HotReloadIntroducedTypeOutcome introducedType in result.IntroducedTypes)
            {
                if (activePatchSiblingFiles.Contains(introducedType.OwnerProjectRelativePath))
                {
                    continue;
                }

                if (introducedType.Kind == HotReloadIntroducedTypeOutcomeKind.Failed)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Chooses the fallback for one run. isPlaying and compileRefusedDuringPlay (Unity's
        /// "Script Changes While Playing" set to recompile only after play ends) are parameters
        /// rather than Editor reads so the decision stays a pure function.
        /// </summary>
        internal static HotReloadCompileFallbackDecision Decide(
            HotReloadCompileOnSkip option,
            bool hasUnappliedEdit,
            bool isPlaying,
            bool compileRefusedDuringPlay)
        {
            // Nothing stayed unapplied, so no option asks for a compile: the run already put every
            // requested edit into the running domain.
            if (!hasUnappliedEdit)
            {
                return HotReloadCompileFallbackDecision.NotNeeded;
            }

            if (option == HotReloadCompileOnSkip.off)
            {
                return HotReloadCompileFallbackDecision.Disabled;
            }

            // Why on does not request a compile here: the compile tool refuses to run during play
            // under this setting, so the CLI would only report a failed compile in place of the
            // step that actually unblocks it.
            if (option == HotReloadCompileOnSkip.on)
            {
                return isPlaying && compileRefusedDuringPlay
                    ? HotReloadCompileFallbackDecision.BlockedByPlayModeSetting
                    : HotReloadCompileFallbackDecision.Requested;
            }

            // Why auto holds during play: a compile reloads the domain and so ends the Play
            // session, and hot reload does not end a session it was not asked to end.
            return isPlaying
                ? HotReloadCompileFallbackDecision.HeldForPlayMode
                : HotReloadCompileFallbackDecision.Requested;
        }
    }
}
