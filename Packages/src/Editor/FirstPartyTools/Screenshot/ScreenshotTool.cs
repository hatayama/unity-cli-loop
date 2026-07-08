using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for Unity Editor screenshots.
    /// </summary>
    [UnityCliLoopTool]
    public class ScreenshotTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
    {
        public override string ToolName => "screenshot";

        protected override async Task<ScreenshotResponse> ExecuteAsync(ScreenshotSchema parameters, CancellationToken ct)
        {
            ScreenshotUseCase useCase = new();
            return await useCase.CaptureAsync(parameters, ct).ConfigureAwait(false);
        }
    }
}
