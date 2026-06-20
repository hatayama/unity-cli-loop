#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Public facade for editor window and Game View capture helpers used by custom tools.
    /// </summary>
    public interface IEditorWindowCaptureService
    {
        EditorWindow[] FindWindowsByName(string windowName, WindowMatchMode matchMode);

        Task<(Texture2D? texture, bool timedOut)> CaptureWindowAsync(
            EditorWindow window,
            float resolutionScale,
            int timeoutMilliseconds,
            CancellationToken ct);

        string[] GetOpenWindowNames();

        Task<(Texture2D? texture, int yOffset, bool timedOut)> CaptureGameRenderingAsync(
            float resolutionScale,
            int timeoutMilliseconds,
            CancellationToken ct);
    }

    /// <summary>
    /// Static capture entrypoint kept in the public tool contract assembly.
    /// </summary>
    public static class EditorWindowCaptureUtility
    {
        private static IEditorWindowCaptureService? ServiceValue;

        internal static void RegisterService(IEditorWindowCaptureService service)
        {
            System.Diagnostics.Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static EditorWindow[] FindWindowsByName(
            string windowName,
            WindowMatchMode matchMode = WindowMatchMode.exact)
        {
            return Service.FindWindowsByName(windowName, matchMode);
        }

        public static Task<(Texture2D? texture, bool timedOut)> CaptureWindowAsync(
            EditorWindow window,
            float resolutionScale,
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            return Service.CaptureWindowAsync(window, resolutionScale, timeoutMilliseconds, ct);
        }

        public static string[] GetOpenWindowNames()
        {
            return Service.GetOpenWindowNames();
        }

        public static Task<(Texture2D? texture, int yOffset, bool timedOut)> CaptureGameRenderingAsync(
            float resolutionScale,
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            return Service.CaptureGameRenderingAsync(resolutionScale, timeoutMilliseconds, ct);
        }

        private static IEditorWindowCaptureService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop editor window capture service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
