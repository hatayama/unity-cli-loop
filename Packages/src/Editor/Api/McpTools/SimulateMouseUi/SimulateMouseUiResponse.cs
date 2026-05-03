#nullable enable

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateMouseUiResponse : BaseToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? HitGameObjectName { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float? EndPositionX { get; set; }
        public float? EndPositionY { get; set; }

        public SimulateMouseUiResponse()
        {
        }
    }
}
