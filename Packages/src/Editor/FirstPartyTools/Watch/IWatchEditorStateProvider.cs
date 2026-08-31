namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Supplies the Editor state required to detect a completed Play Mode Step.
    /// </summary>
    public interface IWatchEditorStateProvider
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
        int FrameCount { get; }
        System.DateTime UtcNow { get; }
    }
}
