using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    // ==========================================================================
    // IMPORTANT: Why this class still exists (Do not delete)
    // ==========================================================================
    // The focus-window functionality is handled at OS level by the CLI using the
    // launch-unity package. This works even when Unity is busy compiling or
    // reloading the domain.
    //
    // This class remains for two reasons:
    //
    // 1. Tool List Registration:
    //    - The CLI tool list is built from Unity's tool registry
    //    - Without this class, "focus-window" won't appear in tools/list
    //
    // 2. Graceful CLI Upgrade Message:
    //    - Old CLI versions connect directly to Unity TCP (bypass TypeScript)
    //    - When old CLI calls this tool, we return an upgrade prompt
    //    - New CLI uses OS-level focus and never reaches this code
    //
    // Execution flow:
    // - New CLI: CLI intercepts → OS-level focus → This code NOT called
    // - Old CLI: Direct Unity TCP → This code called → Returns upgrade message
    // ==========================================================================

    /// <summary>
    /// Stub for tool list registration. Actual focus is handled by the CLI.
    /// </summary>
    [McpTool(Description = "Bring Unity Editor window to front using OS-level commands. Works even when Unity is busy (compiling, domain reload).")]
    public class FocusUnityWindowTool : AbstractUnityTool<FocusUnityWindowSchema, FocusUnityWindowResponse>
    {
        /// <inheritdoc />
        public override string ToolName => "focus-window";

        /// <inheritdoc />
        protected override Task<FocusUnityWindowResponse> ExecuteAsync(
            FocusUnityWindowSchema parameters,
            CancellationToken ct)
        {
            // This code is only reached by old CLI versions that bypass the
            // OS-level focus path.
            return Task.FromResult(new FocusUnityWindowResponse(
                "Please update uloop-cli to the latest version for improved focus-window support. " +
                "Run: npm install -g uloop-cli@latest",
                "focus-window now uses OS-level commands and works even during Unity compilation. " +
                "Update CLI to use this feature."));
        }
    }
}
