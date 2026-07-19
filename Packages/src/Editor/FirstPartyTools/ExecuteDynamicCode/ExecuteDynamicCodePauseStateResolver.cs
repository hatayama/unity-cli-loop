namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Combines the Editor's pause flag with the registry's active-pause-point signal into the
    /// pair exposed on ExecuteDynamicCodeResponse. Kept as a pure, Editor-API-free method so the
    /// contract ("ActivePausePointId is only ever non-empty while EditorPaused is true") can be
    /// unit-tested without a real Unity Editor pause.
    /// </summary>
    internal static class ExecuteDynamicCodePauseStateResolver
    {
        public static (bool EditorPaused, string ActivePausePointId) Resolve(
            bool editorIsPaused, string registryActivePausePointId)
        {
            // Why: a pause point's freeze window can still be open for up to one Editor frame
            // after an external unpause closes it (see
            // UloopPausePointRegistry.ClosePauseWindowIfEditorResumedExternally); without this
            // gate, that narrow window could report a non-empty id while EditorPaused is false.
            return editorIsPaused
                ? (true, registryActivePausePointId ?? string.Empty)
                : (false, string.Empty);
        }
    }
}
