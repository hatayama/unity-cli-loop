namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Reads how long the Unity Editor main thread has gone without a tick, so Application code can
    /// tell a blocked Editor from a busy one without depending on Editor platform types. Safe to read
    /// from any thread.
    /// </summary>
    public interface IMainThreadLivenessPort
    {
        double SecondsSinceLastMainThreadTick();
    }
}
