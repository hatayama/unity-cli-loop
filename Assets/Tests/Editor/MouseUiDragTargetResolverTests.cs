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
    /// Verifies drag target resolution shared by one-shot and incremental mouse UI drags.
    /// </summary>
    [TestFixture]
    public sealed class MouseUiDragTargetResolverTests
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
        /// Verifies bypass resolution preserves the raw raycast target and resolves its hierarchy drag handler.
        /// </summary>
        [Test]
        public void Resolve_WithBypassPathBelowDragHandler_ReturnsHandlerAndRawRaycast()
        {
            EventSystem eventSystem = CreateEventSystem();
            GameObject expectedTarget = CreateGameObject("MouseUiDragTargetResolverTests_Handler");
            expectedTarget.AddComponent<MouseUiDragTargetResolverTestHandler>();
            GameObject rawTarget = CreateGameObject("RawTarget", expectedTarget.transform);
            MouseUiSimulationCommand command = CreateCommand(
                UnityCliLoopMouseUiAction.Drag,
                GameObjectPathUtility.GetFullPath(rawTarget));
            Vector2 position = new(10f, 20f);

            (RaycastResult startRaycast, GameObject? target, SimulateMouseUiResponse? failureResponse) =
                MouseUiDragTargetResolver.Resolve(
                    command,
                    eventSystem,
                    MouseAction.Drag,
                    position,
                    position);

            Assert.That(startRaycast.gameObject, Is.SameAs(rawTarget));
            Assert.That(target, Is.SameAs(expectedTarget));
            Assert.That(failureResponse, Is.Null);
        }

        /// <summary>
        /// Verifies bypass resolution keeps the raw raycast while returning no target when no drag handler exists.
        /// </summary>
        [Test]
        public void Resolve_WithBypassPathWithoutDragHandler_ReturnsRawRaycastWithoutFailure()
        {
            EventSystem eventSystem = CreateEventSystem();
            GameObject rawTarget = CreateGameObject("MouseUiDragTargetResolverTests_NoHandler");
            MouseUiSimulationCommand command = CreateCommand(
                UnityCliLoopMouseUiAction.DragStart,
                GameObjectPathUtility.GetFullPath(rawTarget));
            Vector2 position = new(30f, 40f);

            (RaycastResult startRaycast, GameObject? target, SimulateMouseUiResponse? failureResponse) =
                MouseUiDragTargetResolver.Resolve(
                    command,
                    eventSystem,
                    MouseAction.DragStart,
                    position,
                    position);

            Assert.That(startRaycast.gameObject, Is.SameAs(rawTarget));
            Assert.That(target, Is.Null);
            Assert.That(failureResponse, Is.Null);
        }

        /// <summary>
        /// Verifies a missing bypass path preserves the requested action and input position in the failure response.
        /// </summary>
        [Test]
        public void Resolve_WithMissingBypassPath_ReturnsPathFailure()
        {
            EventSystem eventSystem = CreateEventSystem();
            string targetPath = "MouseUiDragTargetResolverTests_Missing/Target";
            MouseUiSimulationCommand command = CreateCommand(
                UnityCliLoopMouseUiAction.DragStart,
                targetPath);
            Vector2 position = new(50f, 60f);

            (RaycastResult startRaycast, GameObject? target, SimulateMouseUiResponse? failureResponse) =
                MouseUiDragTargetResolver.Resolve(
                    command,
                    eventSystem,
                    MouseAction.DragStart,
                    position,
                    position);

            Assert.That(startRaycast.gameObject, Is.Null);
            Assert.That(target, Is.Null);
            Assert.That(failureResponse, Is.Not.Null);
            Assert.That(failureResponse!.Message, Is.EqualTo($"TargetPath '{targetPath}' was not found."));
            Assert.That(failureResponse.Action, Is.EqualTo(MouseAction.DragStart.ToString()));
            Assert.That(failureResponse.PositionX, Is.EqualTo(position.x));
            Assert.That(failureResponse.PositionY, Is.EqualTo(position.y));
        }

        private EventSystem CreateEventSystem()
        {
            return CreateGameObject("MouseUiDragTargetResolverTests_EventSystem").AddComponent<EventSystem>();
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

        private static MouseUiSimulationCommand CreateCommand(
            UnityCliLoopMouseUiAction action,
            string targetPath)
        {
            (MouseUiSimulationCommand? command, string? errorMessage) =
                MouseUiSimulationCommand.TryFromSchema(new SimulateMouseUiSchema
                {
                    Action = action,
                    BypassRaycast = true,
                    TargetPath = targetPath
                });
            Assert.That(errorMessage, Is.Null);
            Assert.That(command, Is.Not.Null);
            return command!;
        }
    }

    internal sealed class MouseUiDragTargetResolverTestHandler : MonoBehaviour, IDragHandler
    {
        public void OnDrag(PointerEventData eventData)
        {
        }
    }
}
