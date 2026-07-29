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
        public bool WaitForDomainReload { get; set; } = true;

        /// <summary>
        /// Whether to reload or save externally changed open Scene files before compilation refresh.
        /// </summary>
        public bool ReloadExternalSceneChanges { get; set; } = true;

        /// <summary>
        /// How long the CLI waits for compilation to complete, in seconds.
        /// Unity ignores this value; it is consumed by the CLI.
        /// Why no [Description]: first-party schema properties must keep long-form agent guidance
        /// in skill files (see FirstPartyToolSchemaMetadataTests), not runtime metadata.
        /// </summary>
        public int CompileWaitTimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// Internal request identifier used for delayed result recovery across domain reload.
        /// </summary>
        [Browsable(false)]
        public string RequestId { get; set; } = "";
    }
}
