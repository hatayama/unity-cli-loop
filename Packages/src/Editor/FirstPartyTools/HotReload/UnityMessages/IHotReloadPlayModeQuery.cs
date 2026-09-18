namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Whether the editor is playing, asked as a question so a test can answer it without
    /// entering Play Mode.
    /// </summary>
    internal interface IHotReloadPlayModeQuery
    {
        bool IsPlaying { get; }
    }
}
