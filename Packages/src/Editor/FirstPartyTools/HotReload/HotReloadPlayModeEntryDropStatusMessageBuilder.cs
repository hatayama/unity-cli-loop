namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the --status Message replacement when Play-entry domain reload leftovers remain
    /// and no hot-reload changes are currently active.
    /// </summary>
    internal static class HotReloadPlayModeEntryDropStatusMessageBuilder
    {
        public static string Build(int activeCount, int droppedCount)
        {
            if (activeCount != 0 || droppedCount <= 0)
            {
                return null;
            }

            return string.Format(
                HotReloadConstants.PlayModeEntryDropStatusMessageFormat,
                droppedCount);
        }
    }
}
