using System.Collections.Generic;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Validates ScreenshotSchema parameters before capture begins.
    /// </summary>
    internal static class ScreenshotParameterValidator
    {
        internal static void Validate(ScreenshotSchema request)
        {
            if (request.CaptureMode != CaptureMode.rendering &&
                string.IsNullOrEmpty(request.WindowName))
            {
                throw new UnityCliLoopToolParameterValidationException("WindowName cannot be null or empty");
            }

            if (request.ResolutionScale < 0.1f || request.ResolutionScale > 1.0f)
            {
                throw new UnityCliLoopToolParameterValidationException(
                    $"ResolutionScale must be between 0.1 and 1.0, got: {request.ResolutionScale}");
            }

            // AnnotateElements, ElementsOnly, and AnnotateRaycastGrid rely on PlayMode rendering pipeline
            if (request.CaptureMode != CaptureMode.rendering)
            {
                if (request.AnnotateElements)
                {
                    throw new UnityCliLoopToolParameterValidationException("AnnotateElements is only supported when CaptureMode=rendering");
                }

                if (request.ElementsOnly)
                {
                    throw new UnityCliLoopToolParameterValidationException("ElementsOnly is only supported when CaptureMode=rendering");
                }

                if (request.AnnotateRaycastGrid)
                {
                    throw new UnityCliLoopToolParameterValidationException("AnnotateRaycastGrid is only supported when CaptureMode=rendering");
                }
            }

            if (request.ElementsOnly &&
                !request.AnnotateElements &&
                !request.AnnotateRaycastGrid)
            {
                throw new UnityCliLoopToolParameterValidationException(
                    "ElementsOnly requires AnnotateElements=true or AnnotateRaycastGrid=true");
            }

            RaycastLayerMaskResolution raycastLayerMaskResolution = ResolveRaycastLayerMask(request);
            if (raycastLayerMaskResolution.HasLayerNames && !request.AnnotateRaycastGrid)
            {
                throw new UnityCliLoopToolParameterValidationException(
                    "RaycastLayerMask requires AnnotateRaycastGrid=true");
            }

            if (!raycastLayerMaskResolution.IsValid)
            {
                throw new UnityCliLoopToolParameterValidationException(
                    CreateInvalidRaycastLayerMaskMessage(raycastLayerMaskResolution));
            }
        }

        internal static RaycastLayerMaskResolution ResolveRaycastLayerMask(ScreenshotSchema request)
        {
            string raycastLayerMask = request.RaycastLayerMask ?? "";
            return RaycastLayerMaskResolver.Resolve(
                raycastLayerMask,
                GetAvailableLayerDefinitions());
        }

        internal static List<RaycastLayerDefinition> GetAvailableLayerDefinitions()
        {
            List<RaycastLayerDefinition> layerDefinitions = new();
            for (int layerIndex = 0; layerIndex <= 31; layerIndex++)
            {
                string layerName = LayerMask.LayerToName(layerIndex);
                if (string.IsNullOrEmpty(layerName))
                {
                    continue;
                }

                layerDefinitions.Add(new RaycastLayerDefinition
                {
                    Name = layerName,
                    Index = layerIndex
                });
            }

            return layerDefinitions;
        }

        internal static string CreateInvalidRaycastLayerMaskMessage(
            RaycastLayerMaskResolution raycastLayerMaskResolution)
        {
            string invalidLayerNames = string.Join(", ", raycastLayerMaskResolution.InvalidLayerNames);
            string validLayerNames = string.Join(", ", raycastLayerMaskResolution.ValidLayerNames);
            if (string.IsNullOrEmpty(validLayerNames))
            {
                validLayerNames = "(none)";
            }

            return $"RaycastLayerMask contains unknown layer name(s): {invalidLayerNames}. Valid layers: {validLayerNames}";
        }
    }
}
