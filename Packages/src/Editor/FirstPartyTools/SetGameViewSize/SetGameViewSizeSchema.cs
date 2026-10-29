using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the optional resolution parameters accepted by the Set Game View Size tool.
    /// </summary>
    public sealed class SetGameViewSizeSchema : UnityCliLoopToolSchema
    {
        public int? Width { get; set; }

        public int? Height { get; set; }

        /// <summary>
        /// When true, removes the size entries this tool added and restores the previous selection.
        /// Cannot be combined with Width / Height.
        /// </summary>
        public bool Reset { get; set; } = false;
    }
}
