using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for input replay.
    /// </summary>
    [UnityCliLoopTool]
    public class ReplayInputTool : UnityCliLoopTool<ReplayInputSchema, ReplayInputResponse>
    {
        public override string ToolName => "replay-input";

        protected override async Task<ReplayInputResponse> ExecuteAsync(ReplayInputSchema parameters, CancellationToken ct)
        {
            ReplayInputUseCase useCase = new();
            UnityCliLoopReplayInputResult result = await useCase.ReplayInputAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopReplayInputRequest ToRequest(ReplayInputSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopReplayInputRequest
            {
                Action = parameters.Action,
                InputPath = parameters.InputPath,
                ShowOverlay = parameters.ShowOverlay,
                Loop = parameters.Loop,
            };
        }

        private static ReplayInputResponse ToResponse(UnityCliLoopReplayInputResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new ReplayInputResponse
            {
                Success = result.Success,
                Message = result.Message,
                Action = result.Action,
                InputPath = result.InputPath,
                CurrentFrame = result.CurrentFrame,
                TotalFrames = result.TotalFrames,
                Progress = result.Progress,
                IsReplaying = result.IsReplaying,
            };
        }
    }
}
