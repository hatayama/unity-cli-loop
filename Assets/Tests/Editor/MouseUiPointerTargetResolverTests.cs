#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterizes pointer target resolution before it is extracted from the mouse UI use case.
    /// </summary>
    [TestFixture]
    public sealed class MouseUiPointerTargetResolverTests
    {
        private readonly List<GameObject> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                GameObject gameObject = createdObjects[index];
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            createdObjects.Clear();
        }

        /// <summary>
        /// Verifies pointer press data preserves position and runtime mouse button mapping.
        /// </summary>
        [Test]
        public void CreatePointerPressData_WithRightButton_MapsPressState()
        {
            EventSystem eventSystem = CreateEventSystem();
            Vector2 screenPosition = new(12.5f, 34.5f);

            PointerEventData pointerData =
                SimulateMouseUiUseCase.CreatePointerPressData(eventSystem, screenPosition, MouseButton.Right);

            Assert.That(pointerData.position, Is.EqualTo(screenPosition));
            Assert.That(pointerData.pressPosition, Is.EqualTo(screenPosition));
            Assert.That(pointerData.button, Is.EqualTo(PointerEventData.InputButton.Right));
        }

        /// <summary>
        /// Verifies an exact unique hierarchy path resolves to its active GameObject.
        /// </summary>
        [Test]
        public void TryResolveGameObjectPath_WithUniquePath_ReturnsTarget()
        {
            GameObject root = CreateGameObject("MouseUiTargetResolverTests_UniqueRoot");
            GameObject expectedTarget = CreateGameObject("Target", root.transform);
            string targetPath = GameObjectPathUtility.GetFullPath(expectedTarget);
            Vector2 inputPosition = new(10f, 20f);

            bool resolved = SimulateMouseUiUseCase.TryResolveGameObjectPath(
                targetPath,
                "TargetPath",
                MouseAction.Click,
                inputPosition,
                out GameObject? target,
                out SimulateMouseUiResponse? failureResponse);

            Assert.That(resolved, Is.True);
            Assert.That(target, Is.SameAs(expectedTarget));
            Assert.That(failureResponse, Is.Null);
        }

        /// <summary>
        /// Verifies a missing hierarchy path returns the exact wire-visible failure response.
        /// </summary>
        [Test]
        public void TryResolveGameObjectPath_WithMissingPath_ReturnsNotFoundFailure()
        {
            string targetPath = "MouseUiTargetResolverTests_MissingRoot/Target";
            Vector2 inputPosition = new(10f, 20f);

            bool resolved = SimulateMouseUiUseCase.TryResolveGameObjectPath(
                targetPath,
                "TargetPath",
                MouseAction.Click,
                inputPosition,
                out GameObject? target,
                out SimulateMouseUiResponse? failureResponse);

            Assert.That(resolved, Is.False);
            Assert.That(target, Is.Null);
            Assert.That(failureResponse, Is.Not.Null);
            Assert.That(failureResponse!.Success, Is.False);
            Assert.That(failureResponse.Message, Is.EqualTo($"TargetPath '{targetPath}' was not found."));
            Assert.That(failureResponse.Action, Is.EqualTo(MouseAction.Click.ToString()));
            Assert.That(failureResponse.PositionX, Is.EqualTo(inputPosition.x));
            Assert.That(failureResponse.PositionY, Is.EqualTo(inputPosition.y));
        }

        /// <summary>
        /// Verifies duplicate hierarchy paths return the exact ambiguity count and no target.
        /// </summary>
        [Test]
        public void TryResolveGameObjectPath_WithDuplicatePath_ReturnsAmbiguousFailure()
        {
            GameObject firstRoot = CreateGameObject("MouseUiTargetResolverTests_DuplicateRoot");
            GameObject firstTarget = CreateGameObject("Target", firstRoot.transform);
            GameObject secondRoot = CreateGameObject("MouseUiTargetResolverTests_DuplicateRoot");
            CreateGameObject("Target", secondRoot.transform);
            string targetPath = GameObjectPathUtility.GetFullPath(firstTarget);
            Vector2 inputPosition = new(10f, 20f);

            bool resolved = SimulateMouseUiUseCase.TryResolveGameObjectPath(
                targetPath,
                "TargetPath",
                MouseAction.Click,
                inputPosition,
                out GameObject? target,
                out SimulateMouseUiResponse? failureResponse);

            Assert.That(resolved, Is.False);
            Assert.That(target, Is.Null);
            Assert.That(failureResponse, Is.Not.Null);
            Assert.That(
                failureResponse!.Message,
                Is.EqualTo(
                    $"TargetPath '{targetPath}' matched 2 active GameObjects. Use a unique hierarchy path."));
        }

        /// <summary>
        /// Verifies an empty drop path succeeds without resolving an explicit target.
        /// </summary>
        [Test]
        public void TryResolveDropTargetPath_WithoutPath_ReturnsNoTarget()
        {
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Drag,
                DropTargetPath = " "
            });

            bool resolved = SimulateMouseUiUseCase.TryResolveDropTargetPath(
                command,
                MouseAction.Drag,
                new Vector2(10f, 20f),
                out GameObject? dropTarget,
                out SimulateMouseUiResponse? failureResponse);

            Assert.That(resolved, Is.True);
            Assert.That(dropTarget, Is.Null);
            Assert.That(failureResponse, Is.Null);
        }

        /// <summary>
        /// Verifies an explicit drop target without a handler returns the exact failure response.
        /// </summary>
        [Test]
        public void TryResolveDropTargetPath_WithoutDropHandler_ReturnsFailure()
        {
            GameObject root = CreateGameObject("MouseUiTargetResolverTests_DropRoot");
            GameObject target = CreateGameObject("DropTarget", root.transform);
            string targetPath = GameObjectPathUtility.GetFullPath(target);
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Drag,
                DropTargetPath = targetPath
            });
            Vector2 inputPosition = new(10f, 20f);

            bool resolved = SimulateMouseUiUseCase.TryResolveDropTargetPath(
                command,
                MouseAction.Drag,
                inputPosition,
                out GameObject? dropTarget,
                out SimulateMouseUiResponse? failureResponse);

            Assert.That(resolved, Is.False);
            Assert.That(dropTarget, Is.Null);
            Assert.That(failureResponse, Is.Not.Null);
            Assert.That(
                failureResponse!.Message,
                Is.EqualTo($"DropTargetPath '{targetPath}' has no drop handler."));
            Assert.That(failureResponse.Action, Is.EqualTo(MouseAction.Drag.ToString()));
            Assert.That(failureResponse.PositionX, Is.EqualTo(inputPosition.x));
            Assert.That(failureResponse.PositionY, Is.EqualTo(inputPosition.y));
        }

        /// <summary>
        /// Verifies an explicit drop target with a hierarchy handler returns its raw target.
        /// </summary>
        [Test]
        public void TryResolveDropTargetPath_WithDropHandler_ReturnsRawTarget()
        {
            GameObject root = CreateGameObject("MouseUiTargetResolverTests_DropHandlerRoot");
            root.AddComponent<MouseUiPointerTargetResolverTestHandler>();
            GameObject expectedTarget = CreateGameObject("DropTarget", root.transform);
            string targetPath = GameObjectPathUtility.GetFullPath(expectedTarget);
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Drag,
                DropTargetPath = targetPath
            });

            bool resolved = SimulateMouseUiUseCase.TryResolveDropTargetPath(
                command,
                MouseAction.Drag,
                new Vector2(10f, 20f),
                out GameObject? dropTarget,
                out SimulateMouseUiResponse? failureResponse);

            Assert.That(resolved, Is.True);
            Assert.That(dropTarget, Is.SameAs(expectedTarget));
            Assert.That(failureResponse, Is.Null);
        }

        /// <summary>
        /// Verifies bypass resolution preserves raw and hierarchy handlers in pointer state and result DTO.
        /// </summary>
        [Test]
        public void ResolvePressablePointerTargets_WithBypassPath_MapsHierarchyHandlers()
        {
            EventSystem eventSystem = CreateEventSystem();
            GameObject handlerTarget = CreateGameObject("MouseUiTargetResolverTests_PressHandler");
            handlerTarget.AddComponent<MouseUiPointerTargetResolverTestHandler>();
            GameObject rawTarget = CreateGameObject("RawTarget", handlerTarget.transform);
            string targetPath = GameObjectPathUtility.GetFullPath(rawTarget);
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Click,
                BypassRaycast = true,
                TargetPath = targetPath
            });
            Vector2 position = new(10f, 20f);
            PointerEventData pointerData =
                SimulateMouseUiUseCase.CreatePointerPressData(eventSystem, position, MouseButton.Left);

            SimulateMouseUiUseCase.ResolvedPointerTargets resolvedTargets =
                SimulateMouseUiUseCase.ResolvePressablePointerTargets(
                    command,
                    eventSystem,
                    position,
                    position,
                    pointerData,
                    MouseAction.Click);

            Assert.That(resolvedTargets.RawTarget, Is.SameAs(rawTarget));
            Assert.That(resolvedTargets.PressTarget, Is.SameAs(handlerTarget));
            Assert.That(resolvedTargets.ClickTarget, Is.SameAs(handlerTarget));
            Assert.That(resolvedTargets.Target, Is.SameAs(handlerTarget));
            Assert.That(resolvedTargets.FailureResponse, Is.Null);
            Assert.That(pointerData.pointerCurrentRaycast.gameObject, Is.SameAs(rawTarget));
            Assert.That(pointerData.pointerPressRaycast.gameObject, Is.SameAs(rawTarget));
            Assert.That(pointerData.pointerPress, Is.SameAs(handlerTarget));
            Assert.That(pointerData.rawPointerPress, Is.SameAs(rawTarget));
        }

        /// <summary>
        /// Verifies non-bypass resolution returns an empty DTO when no raycaster reports a hit.
        /// </summary>
        [Test]
        public void ResolvePressablePointerTargets_WithoutRaycastHit_ReturnsEmpty()
        {
            EventSystem eventSystem = CreateEventSystem();
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Click
            });
            Vector2 position = new(10f, 20f);
            PointerEventData pointerData =
                SimulateMouseUiUseCase.CreatePointerPressData(eventSystem, position, MouseButton.Left);

            SimulateMouseUiUseCase.ResolvedPointerTargets resolvedTargets =
                SimulateMouseUiUseCase.ResolvePressablePointerTargets(
                    command,
                    eventSystem,
                    position,
                    position,
                    pointerData,
                    MouseAction.Click);

            Assert.That(resolvedTargets.RawTarget, Is.Null);
            Assert.That(resolvedTargets.PressTarget, Is.Null);
            Assert.That(resolvedTargets.ClickTarget, Is.Null);
            Assert.That(resolvedTargets.Target, Is.Null);
            Assert.That(resolvedTargets.FailureResponse, Is.Null);
        }

        /// <summary>
        /// Verifies direct raycast construction carries the supplied GameObject.
        /// </summary>
        [Test]
        public void CreateDirectRaycastResult_WithTarget_MapsGameObject()
        {
            GameObject target = CreateGameObject("MouseUiTargetResolverTests_DirectRaycastTarget");

            RaycastResult result = SimulateMouseUiUseCase.CreateDirectRaycastResult(target);

            Assert.That(result.gameObject, Is.SameAs(target));
        }

        private EventSystem CreateEventSystem()
        {
            GameObject gameObject = CreateGameObject("MouseUiTargetResolverTests_EventSystem");
            return gameObject.AddComponent<EventSystem>();
        }

        private GameObject CreateGameObject(string name, Transform? parent = null)
        {
            GameObject gameObject = new(name);
            createdObjects.Add(gameObject);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent);
            }

            return gameObject;
        }

        private static MouseUiSimulationCommand CreateCommand(SimulateMouseUiSchema schema)
        {
            (MouseUiSimulationCommand? command, string? errorMessage) =
                MouseUiSimulationCommand.TryFromSchema(schema);
            Assert.That(errorMessage, Is.Null);
            Assert.That(command, Is.Not.Null);
            return command!;
        }
    }

    internal sealed class MouseUiPointerTargetResolverTestHandler :
        MonoBehaviour,
        IPointerDownHandler,
        IPointerClickHandler,
        IDropHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
        }

        public void OnPointerClick(PointerEventData eventData)
        {
        }

        public void OnDrop(PointerEventData eventData)
        {
        }
    }
}
