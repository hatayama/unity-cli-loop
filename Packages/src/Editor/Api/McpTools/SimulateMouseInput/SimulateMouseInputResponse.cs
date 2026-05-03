#nullable enable

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateMouseInputResponse : BaseToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Button { get; set; }
        public float? PositionX { get; set; }
        public float? PositionY { get; set; }

        public SimulateMouseInputResponse()
        {
        }
    }
}
