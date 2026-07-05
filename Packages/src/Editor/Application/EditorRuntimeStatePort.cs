namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Reads live Unity Editor runtime state so Application code can consult compile/update/play-mode
    /// state without depending on Editor platform types directly.
    /// </summary>
    public interface IEditorRuntimeStatePort
    {
        bool IsCompiling { get; }
        bool IsUpdating { get; }
        bool IsPlaying { get; }
        bool IsPaused { get; }
    }
}
