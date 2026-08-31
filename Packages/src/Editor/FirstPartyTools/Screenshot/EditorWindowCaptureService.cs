#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;
using ToolContractsCaptureUtility = io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bridges the public capture contract to the bundled screenshot implementation.
    /// </summary>
    internal sealed class EditorWindowCaptureService : IEditorWindowCaptureService
    {
        public EditorWindow[] FindWindowsByName(string windowName, WindowMatchMode matchMode)
        {
            return EditorWindowCaptureUtility.FindWindowsByName(windowName, matchMode);
        }

        public Task<(Texture2D? texture, bool timedOut)> CaptureWindowAsync(
            EditorWindow window,
            float resolutionScale,
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            return EditorWindowCaptureUtility.CaptureWindowAsync(
                window,
                resolutionScale,
                timeoutMilliseconds,
                ct);
        }

        public string[] GetOpenWindowNames()
        {
            return EditorWindowCaptureUtility.GetOpenWindowNames();
        }

        public async Task<(Texture2D? texture, int yOffset, bool timedOut)> CaptureGameRenderingAsync(
            float resolutionScale,
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            (Texture2D? texture, GameRenderingImageInfo renderingImageInfo, bool timedOut) =
                await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
                    resolutionScale,
                    null,
                    timeoutMilliseconds,
                    ct).ConfigureAwait(false);
            return (texture, renderingImageInfo.ImageToInputOffsetY, timedOut);
        }
    }

    /// <summary>
    /// Registers screenshot services for public contract facades.
    /// </summary>
    internal static class ScreenshotEditorStartup
    {
        internal static void Initialize()
        {
            ToolContractsCaptureUtility.RegisterService(new EditorWindowCaptureService());
        }
    }
}
