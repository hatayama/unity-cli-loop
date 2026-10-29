using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the resolution before and after a Set Game View Size request.
    /// </summary>
    public sealed class SetGameViewSizeResponse : UnityCliLoopToolResponse
    {
        /// <summary>
        /// Play Mode view after the request: GameView, Simulator, None, or Other.
        /// </summary>
        public string View { get; set; } = "";

        public uint PreviousWidth { get; set; }
        public uint PreviousHeight { get; set; }
        public uint CurrentWidth { get; set; }
        public uint CurrentHeight { get; set; }
        public bool Changed { get; set; }

        /// <summary>
        /// Display name of the entry selected in the Game View size dropdown after the request.
        /// Present when a Game View is showing.
        /// </summary>
        public string SelectedSize { get; set; }

        public bool ShouldSerializeSelectedSize()
        {
            return !string.IsNullOrEmpty(SelectedSize);
        }

        /// <summary>
        /// Number of uloop size entries removed. Present with --reset.
        /// </summary>
        public int? Removed { get; set; }

        public bool ShouldSerializeRemoved()
        {
            return Removed.HasValue;
        }

        public string Message { get; set; } = "";

        public string Warning { get; set; } = "";

        public bool ShouldSerializeWarning()
        {
            return !string.IsNullOrEmpty(Warning);
        }
    }
}
