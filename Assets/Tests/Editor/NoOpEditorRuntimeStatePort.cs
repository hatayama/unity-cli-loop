using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Reports every Editor runtime state as false. Used by tests that construct
    /// UnityCliLoopToolExecutionService but never exercise its compile/update/play-mode guarded paths.
    /// </summary>
    internal sealed class NoOpEditorRuntimeStatePort : IEditorRuntimeStatePort
    {
        public bool IsCompiling => false;
        public bool IsUpdating => false;
        public bool IsPlaying => false;
        public bool IsPaused => false;
    }
}
