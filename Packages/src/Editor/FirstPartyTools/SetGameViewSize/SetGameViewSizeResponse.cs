using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the resolution before and after a Set Game View Size request.
    /// </summary>
    public sealed class SetGameViewSizeResponse : UnityCliLoopToolResponse
    {
        public uint PreviousWidth { get; set; }
        public uint PreviousHeight { get; set; }
        public uint CurrentWidth { get; set; }
        public uint CurrentHeight { get; set; }
        public bool Changed { get; set; }
        public string Message { get; set; } = "";
    }
}
