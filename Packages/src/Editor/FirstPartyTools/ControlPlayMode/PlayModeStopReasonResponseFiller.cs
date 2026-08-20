namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Copies a confirmed Play Mode stop reason onto control-play-mode responses when the spec says to.
    /// </summary>
    internal static class PlayModeStopReasonResponseFiller
    {
        internal static bool ShouldCopyConfirmedReason(
            PlayModeAction action,
            bool wasAlreadyStopped,
            bool isPlaying)
        {
            if (action == PlayModeAction.Stop && wasAlreadyStopped)
            {
                return true;
            }

            return action == PlayModeAction.Status && !isPlaying;
        }

        internal static void CopyConfirmedIfNeeded(
            ControlPlayModeResponse response,
            PlayModeAction action,
            bool wasAlreadyStopped)
        {
            if (!ShouldCopyConfirmedReason(action, wasAlreadyStopped, response.IsPlaying))
            {
                return;
            }

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            if (!record.HasValue)
            {
                return;
            }

            response.StoppedBy = record.StoppedBy;
            response.StoppedAt = record.StoppedAtUtc;
        }
    }
}
