using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds annotated-element payloads for screenshot responses.
    /// </summary>
    internal static class ScreenshotResponseFactory
    {
        internal static List<UIElementInfo> CreateResponseAnnotatedElements(
            List<UIElementInfo> uiElements,
            List<UIElementInfo> physicsColliderElements)
        {
            List<UIElementInfo> responseElements = new(uiElements);
            responseElements.AddRange(physicsColliderElements);
            return responseElements;
        }
    }
}
