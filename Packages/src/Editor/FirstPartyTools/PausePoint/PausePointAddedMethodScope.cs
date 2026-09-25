using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Looks up the method hot reload added whose edited source range holds a pause point line,
    /// so the edited-line resolver can refuse that line before it reaches compiled code.
    /// </summary>
    internal static class PausePointAddedMethodScope
    {
        /// <summary>
        /// Returns the method hot reload added to the file whose source range holds the line, or
        /// null when there is none or no hot reload side is installed.
        /// </summary>
        internal static HotReloadAddedMethodAtLine FindAddedMethodContainingLineOrNull(string normalizedFile, int line)
        {
            return HotReloadPausePointCoordination.HotReloadSide?.FindAddedMethodContainingLine(
                normalizedFile,
                line);
        }
    }
}
