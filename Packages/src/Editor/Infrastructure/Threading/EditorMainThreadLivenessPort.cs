using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Exposes the Editor main-thread tick tracker to Application code through its port.
    /// </summary>
    internal sealed class EditorMainThreadLivenessPort : IMainThreadLivenessPort
    {
        public double SecondsSinceLastMainThreadTick()
        {
            return EditorMainThreadLivenessTracker.SecondsSinceLastMainThreadTick();
        }
    }
}
