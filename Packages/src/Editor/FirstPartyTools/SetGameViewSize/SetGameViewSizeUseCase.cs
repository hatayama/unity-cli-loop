using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies and reports the Unity Game View custom rendering resolution.
    /// </summary>
    public sealed class SetGameViewSizeUseCase
    {
        private const string RenderingResolutionBaseName = "uloop";

        /// <summary>
        /// Reads the current resolution and optionally applies a paired width and height.
        /// </summary>
        public Task<SetGameViewSizeResponse> ExecuteAsync(
            SetGameViewSizeSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            (uint previousWidth, uint previousHeight) = GetRenderingResolution();
            bool hasWidth = parameters.Width.HasValue;
            bool hasHeight = parameters.Height.HasValue;
            if (hasWidth != hasHeight)
            {
                return Task.FromResult(CreateResponse(
                    false,
                    "Width and Height must be provided together.",
                    previousWidth,
                    previousHeight,
                    previousWidth,
                    previousHeight));
            }

            if (!hasWidth)
            {
                return Task.FromResult(CreateResponse(
                    true,
                    $"Current Game View rendering resolution is {previousWidth}x{previousHeight}.",
                    previousWidth,
                    previousHeight,
                    previousWidth,
                    previousHeight));
            }

            if (parameters.Width.Value <= 0 || parameters.Height.Value <= 0)
            {
                return Task.FromResult(CreateResponse(
                    false,
                    "Width and Height must be positive integers.",
                    previousWidth,
                    previousHeight,
                    previousWidth,
                    previousHeight));
            }

            PlayModeWindow.SetCustomRenderingResolution(
                (uint)parameters.Width.Value,
                (uint)parameters.Height.Value,
                RenderingResolutionBaseName);

            (uint currentWidth, uint currentHeight) = GetRenderingResolution();
            return Task.FromResult(CreateResponse(
                true,
                $"Game View rendering resolution changed from {previousWidth}x{previousHeight} to {currentWidth}x{currentHeight}.",
                previousWidth,
                previousHeight,
                currentWidth,
                currentHeight));
        }

        private static (uint Width, uint Height) GetRenderingResolution()
        {
            uint width;
            uint height;
            PlayModeWindow.GetRenderingResolution(out width, out height);
            return (width, height);
        }

        private static SetGameViewSizeResponse CreateResponse(
            bool success,
            string message,
            uint previousWidth,
            uint previousHeight,
            uint currentWidth,
            uint currentHeight)
        {
            return new SetGameViewSizeResponse
            {
                Success = success,
                Message = message,
                PreviousWidth = previousWidth,
                PreviousHeight = previousHeight,
                CurrentWidth = currentWidth,
                CurrentHeight = currentHeight,
                Changed = previousWidth != currentWidth || previousHeight != currentHeight
            };
        }
    }
}
