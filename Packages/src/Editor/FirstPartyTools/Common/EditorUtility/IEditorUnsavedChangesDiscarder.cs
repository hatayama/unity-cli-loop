namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Discards dirty Editor Scene / Prefab Stage state without user prompts, restoring disk contents.
    /// </summary>
    public interface IEditorUnsavedChangesDiscarder
    {
        /// <summary>
        /// Discards dirty loaded Scenes and the current Prefab Stage.
        /// Returns display names that could not be discarded; empty when every dirty item was discarded.
        /// </summary>
        string[] DiscardUnsavedEditorChanges();
    }
}
