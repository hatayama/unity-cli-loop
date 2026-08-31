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
    }
}
