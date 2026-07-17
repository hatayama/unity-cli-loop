namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Base response class for Unity CLI tool responses.
    /// </summary>
    public abstract class UnityCliLoopToolResponse
    {
        public bool Success { get; set; } = true;
    }

    public interface IUnityCliLoopTimingResponse
    {
        bool EmitsTimingsInJsonResponse { get; }

        void AddTiming(string timing);
    }
}
