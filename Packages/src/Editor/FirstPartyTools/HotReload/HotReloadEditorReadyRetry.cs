namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides whether an apply response asks the CLI to wait for the Editor to settle and apply
    /// the same request again.
    /// </summary>
    internal static class HotReloadEditorReadyRetry
    {
        /// <summary>
        /// True only when every failure of the run is the Editor compiling or importing. A failure
        /// of any other kind, alone or mixed in, gives the same result after a wait, and a failure
        /// that lost its kind is treated as one the reader fixes.
        /// </summary>
        internal static bool Decide(bool hasFailure, HotReloadFailureKinds failureKinds)
        {
            return hasFailure && failureKinds == HotReloadFailureKinds.EditorNotReady;
        }
    }
}
