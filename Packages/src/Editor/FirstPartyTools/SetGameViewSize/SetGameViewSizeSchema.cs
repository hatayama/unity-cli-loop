using System.ComponentModel;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the optional resolution parameters accepted by the Set Game View Size tool.
    /// </summary>
    public sealed class SetGameViewSizeSchema : UnityCliLoopToolSchema
    {
        [Description("Target Game View rendering width in pixels. Provide with Height to change the resolution.")]
        public int? Width { get; set; }

        [Description("Target Game View rendering height in pixels. Provide with Width to change the resolution.")]
        public int? Height { get; set; }
    }
}
