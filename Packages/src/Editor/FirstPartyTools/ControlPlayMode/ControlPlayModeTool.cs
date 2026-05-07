using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Exposes the Control Play Mode workflow as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class ControlPlayModeTool : UnityCliLoopTool<ControlPlayModeSchema, ControlPlayModeResponse>
    {
        public override string ToolName => "control-play-mode";

        protected override async Task<ControlPlayModeResponse> ExecuteAsync(ControlPlayModeSchema parameters, CancellationToken ct)
        {
            ControlPlayModeUseCase useCase = new();
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
