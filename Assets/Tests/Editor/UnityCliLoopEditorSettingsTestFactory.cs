using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Creates editor settings services backed by the real test project settings files.
    /// </summary>
    internal static class UnityCliLoopEditorSettingsTestFactory
    {
        internal static UnityCliLoopEditorSettingsService CreateService()
        {
            return new UnityCliLoopEditorSettingsService(new UnityCliLoopEditorSettingsRepository());
        }

        internal static UnityCliLoopEditorSettingsService CreateServiceWithRepository(
            out UnityCliLoopEditorSettingsRepository repository)
        {
            repository = new UnityCliLoopEditorSettingsRepository();
            return new UnityCliLoopEditorSettingsService(repository);
        }
    }
}
