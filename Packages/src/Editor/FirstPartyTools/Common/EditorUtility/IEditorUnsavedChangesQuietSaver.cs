namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects and quietly saves dirty Editor Scene / Prefab Stage state without user prompts.
    /// </summary>
    public interface IEditorUnsavedChangesQuietSaver
    {
        string[] DetectUnsavedEditorChanges();

        /// <summary>
        /// Saves dirty loaded Scenes and the current Prefab Stage.
        /// Returns display paths that could not be saved; empty when every dirty item saved.
        /// </summary>
        string[] SaveUnsavedEditorChanges();
    }
}
