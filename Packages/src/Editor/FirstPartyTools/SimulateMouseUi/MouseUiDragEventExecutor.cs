#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes pointer event mechanics shared by one-shot and incremental UI drags.
    /// </summary>
    internal static class MouseUiDragEventExecutor
    {
        internal static PointerEventData InitiateDrag(
            EventSystem eventSystem,
            Vector2 screenPos,
            RaycastResult raycastResult,
            GameObject dragTarget,
            PointerEventData.InputButton inputButton)
        {
            PointerEventData pointerData = new(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = inputButton,
                pointerCurrentRaycast = raycastResult,
                pointerPressRaycast = raycastResult,
                pointerDrag = dragTarget,
                rawPointerPress = raycastResult.gameObject
            };

            // Slider.OnPointerDown initializes m_Offset for handle positioning
            GameObject? pressTarget = ExecuteEvents.ExecuteHierarchy(
                raycastResult.gameObject, pointerData, ExecuteEvents.pointerDownHandler);
            pointerData.pointerPress = pressTarget;

            // ScrollRect.OnInitializePotentialDrag clears inertia, Slider sets useDragThreshold=false
            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.initializePotentialDrag);

            return pointerData;
        }

        // Lifecycle must match StandaloneInputModule: raycast → pointerUp → drop → endDrag
        internal static void FinalizeDrag(
            PointerEventData pointerData,
            GameObject target,
            GameObject? explicitDropTarget)
        {
            if (explicitDropTarget != null)
            {
                pointerData.pointerCurrentRaycast = MouseUiPointerTargetResolver.CreateDirectRaycastResult(explicitDropTarget);
            }
            else
            {
                UpdatePointerRaycast(pointerData);
            }

            if (pointerData.pointerPress != null)
            {
                ExecuteEvents.Execute(pointerData.pointerPress, pointerData, ExecuteEvents.pointerUpHandler);
            }

            // Standard IDropHandler dispatch so Unity drop targets respond without manual workarounds
            GameObject? dropTarget = pointerData.pointerCurrentRaycast.gameObject;
            if (dropTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(dropTarget, pointerData, ExecuteEvents.dropHandler);
            }

            pointerData.dragging = false;
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.endDragHandler);
        }

        private static void UpdatePointerRaycast(PointerEventData pointerData)
        {
            RaycastResult? hit = UiRaycastHelper.RaycastUI(pointerData.position, EventSystem.current);
            pointerData.pointerCurrentRaycast = hit ?? new RaycastResult();
        }

        internal static async Task<bool> InterpolateDragPosition(
            PointerEventData pointerData,
            GameObject target,
            Vector2 endPos,
            float dragSpeed,
            CancellationToken ct)
        {
            Debug.Assert(dragSpeed >= 0f, "dragSpeed must be non-negative");

            Vector2 startPos = pointerData.position;
            float distance = Vector2.Distance(startPos, endPos);
            float duration = dragSpeed > 0f ? distance / dragSpeed : 0f;

            if (duration <= 0f)
            {
                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    return false;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                Vector2 previousPosition = pointerData.position;
                pointerData.position = endPos;
                pointerData.delta = endPos - previousPosition;
                ExecuteEvents.Execute(target, pointerData, ExecuteEvents.dragHandler);

                SimulateMouseUiOverlayState.UpdatePosition(MouseUiCoordinateConverter.ScreenToInput(endPos));
                return true;
            }

            float startTime = Time.realtimeSinceStartup;
            float t;

            do
            {
                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    return false;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                float elapsed = Time.realtimeSinceStartup - startTime;
                t = Mathf.Clamp01(elapsed / duration);
                Vector2 previousPosition = pointerData.position;
                Vector2 currentPosition = Vector2.Lerp(startPos, endPos, t);

                pointerData.position = currentPosition;
                pointerData.delta = currentPosition - previousPosition;

                ExecuteEvents.Execute(target, pointerData, ExecuteEvents.dragHandler);

                SimulateMouseUiOverlayState.UpdatePosition(MouseUiCoordinateConverter.ScreenToInput(currentPosition));
            }
            while (t < 1.0f);

            return true;
        }
    }
}
