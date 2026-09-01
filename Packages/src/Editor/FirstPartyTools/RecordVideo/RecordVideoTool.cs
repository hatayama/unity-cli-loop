using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records the Unity Game View to a video file while Play Mode runs.
    /// </summary>
    [UnityCliLoopTool]
    public sealed class RecordVideoTool : UnityCliLoopTool<RecordVideoSchema, RecordVideoResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_RECORD_VIDEO;

        protected override Task<RecordVideoResponse> ExecuteAsync(
            RecordVideoSchema parameters,
            CancellationToken ct)
        {
            RecordVideoUseCase useCase = new RecordVideoUseCase();
            return useCase.ExecuteAsync(parameters, ct);
        }
    }
}
