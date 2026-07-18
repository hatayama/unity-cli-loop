using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Sets or reports the Unity Game View custom rendering resolution.
    /// </summary>
    [UnityCliLoopTool]
    public sealed class SetGameViewSizeTool : UnityCliLoopTool<SetGameViewSizeSchema, SetGameViewSizeResponse>
    {
        public override string ToolName => "set-game-view-size";

        protected override Task<SetGameViewSizeResponse> ExecuteAsync(
            SetGameViewSizeSchema parameters,
            CancellationToken ct)
        {
            SetGameViewSizeUseCase useCase = new();
            return useCase.ExecuteAsync(parameters, ct);
        }
    }
}
