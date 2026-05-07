using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for input recording.
    /// </summary>
    [UnityCliLoopTool]
    public class RecordInputTool : UnityCliLoopTool<RecordInputSchema, RecordInputResponse>
    {
        public override string ToolName => "record-input";

        protected override async Task<RecordInputResponse> ExecuteAsync(RecordInputSchema parameters, CancellationToken ct)
        {
            RecordInputUseCase useCase = new();
            UnityCliLoopRecordInputResult result = await useCase.RecordInputAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopRecordInputRequest ToRequest(RecordInputSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopRecordInputRequest
            {
                Action = parameters.Action,
                OutputPath = parameters.OutputPath,
                Keys = parameters.Keys,
                DelaySeconds = parameters.DelaySeconds,
                ShowOverlay = parameters.ShowOverlay,
            };
        }

        private static RecordInputResponse ToResponse(UnityCliLoopRecordInputResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new RecordInputResponse
            {
                Success = result.Success,
                Message = result.Message,
                Action = result.Action,
                OutputPath = result.OutputPath,
                TotalFrames = result.TotalFrames,
                DurationSeconds = result.DurationSeconds,
            };
        }
    }
}
