using System.Collections.Generic;
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
            UnityCliLoopScreenshotResult result = await useCase.CaptureAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopScreenshotRequest ToRequest(ScreenshotSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopScreenshotRequest
            {
                WindowName = parameters.WindowName,
                ResolutionScale = parameters.ResolutionScale,
                MatchMode = parameters.MatchMode,
                OutputDirectory = parameters.OutputDirectory,
                CaptureMode = parameters.CaptureMode,
                AnnotateElements = parameters.AnnotateElements,
                ElementsOnly = parameters.ElementsOnly,
            };
        }

        private static ScreenshotResponse ToResponse(UnityCliLoopScreenshotResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new ScreenshotResponse
            {
                Screenshots = ToScreenshotInfos(result.Screenshots),
            };
        }

        private static List<ScreenshotInfo> ToScreenshotInfos(List<UnityCliLoopScreenshotInfo> sourceInfos)
        {
            if (sourceInfos == null)
            {
                return new List<ScreenshotInfo>();
            }

            List<ScreenshotInfo> screenshotInfos = new();
            foreach (UnityCliLoopScreenshotInfo sourceInfo in sourceInfos)
            {
                screenshotInfos.Add(ToScreenshotInfo(sourceInfo));
            }

            return screenshotInfos;
        }

        private static ScreenshotInfo ToScreenshotInfo(UnityCliLoopScreenshotInfo sourceInfo)
        {
            if (sourceInfo == null)
            {
                throw new System.ArgumentNullException(nameof(sourceInfo));
            }

            return new ScreenshotInfo
            {
                ImagePath = sourceInfo.ImagePath,
                FileSizeBytes = sourceInfo.FileSizeBytes,
                Width = sourceInfo.Width,
                Height = sourceInfo.Height,
                CoordinateSystem = sourceInfo.CoordinateSystem,
                ResolutionScale = sourceInfo.ResolutionScale,
                YOffset = sourceInfo.YOffset,
                AnnotatedElements = sourceInfo.AnnotatedElements,
            };
        }
    }
}
