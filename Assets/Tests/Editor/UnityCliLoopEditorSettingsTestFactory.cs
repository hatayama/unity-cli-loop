using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Creates editor settings ports backed by the real test project settings files.
    /// </summary>
    internal static class UnityCliLoopEditorSettingsTestFactory
    {
        internal static IUnityCliLoopEditorSettingsPort CreatePort()
        {
            return new UnityCliLoopEditorSettingsRepository();
        }

        internal static IUnityCliLoopEditorSettingsPort CreatePortWithRepository(
            out UnityCliLoopEditorSettingsRepository repository)
        {
            repository = new UnityCliLoopEditorSettingsRepository();
            return repository;
        }
    }
}
