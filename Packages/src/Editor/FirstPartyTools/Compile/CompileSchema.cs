using System.ComponentModel;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schema for Compile command parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class CompileSchema : UnityCliLoopToolSchema
    {
        /// <summary>
        /// Whether to perform forced recompilation
        /// </summary>
        public bool ForceRecompile { get; set; } = false;

        /// <summary>
        /// Whether to wait for domain reload completion before the caller returns.
        /// </summary>
        public bool WaitForDomainReload { get; set; } = false;

        /// <summary>
        /// Internal request identifier used for delayed result recovery across domain reload.
        /// </summary>
        [Browsable(false)]
        public string RequestId { get; set; } = "";
    }
}
