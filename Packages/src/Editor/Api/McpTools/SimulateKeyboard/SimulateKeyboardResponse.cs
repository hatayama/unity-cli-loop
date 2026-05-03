#nullable enable

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateKeyboardResponse : BaseToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? KeyName { get; set; }

        public SimulateKeyboardResponse()
        {
        }
    }
}
