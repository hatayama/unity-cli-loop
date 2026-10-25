using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    // Centralizes production Editor startup so Unity's unordered hooks do not decide boot order.
    /// <summary>
    /// Provides Unity Loop Harness Editor Bootstrap behavior for Unity Loop Harness.
    /// </summary>
    internal static class UnityCliLoopEditorBootstrap
    {
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            UnityCliLoopEditorBootstrapper bootstrapper = new();
            bootstrapper.Initialize();
        }
    }
}
