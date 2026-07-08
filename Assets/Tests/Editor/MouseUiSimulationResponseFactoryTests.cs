#nullable enable
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterizes wire-visible mouse UI response construction before factory extraction.
    /// </summary>
    [TestFixture]
    public sealed class MouseUiSimulationResponseFactoryTests
    {
        /// <summary>
        /// Verifies generic validation failures preserve the command action and supplied message.
        /// </summary>
        [Test]
        public void CreateFailure_WithCommandAndMessage_MapsFailureResponse()
        {
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.LongPress
            });

            SimulateMouseUiResponse response =
                MouseUiSimulationValidator.CreateFailure(command, "Validation failed.");

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Validation failed."));
            Assert.That(response.Action, Is.EqualTo(MouseAction.LongPress.ToString()));
        }

        /// <summary>
        /// Verifies frame timeout responses preserve the hit and complete drag coordinates.
        /// </summary>
        [Test]
        public void CreateFrameTimeoutResult_WithEndPosition_MapsAllCoordinates()
        {
            Vector2 position = new(10.25f, 20.75f);
            Vector2 endPosition = new(30.5f, 40.25f);

            SimulateMouseUiResponse response = SimulateMouseUiUseCase.CreateFrameTimeoutResult(
                MouseAction.Drag,
                position,
                endPosition,
                "DragTarget");

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    $"Timed out after {UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS}ms while waiting for an editor frame."));
            Assert.That(response.Action, Is.EqualTo(MouseAction.Drag.ToString()));
            Assert.That(response.HitGameObjectName, Is.EqualTo("DragTarget"));
            Assert.That(response.PositionX, Is.EqualTo(position.x));
            Assert.That(response.PositionY, Is.EqualTo(position.y));
            Assert.That(response.EndPositionX, Is.EqualTo(endPosition.x));
            Assert.That(response.EndPositionY, Is.EqualTo(endPosition.y));
        }

        /// <summary>
        /// Verifies frame timeout responses keep optional hit and end coordinates absent.
        /// </summary>
        [Test]
        public void CreateFrameTimeoutResult_WithoutEndPosition_LeavesOptionalFieldsNull()
        {
            Vector2 position = new(10.25f, 20.75f);

            SimulateMouseUiResponse response = SimulateMouseUiUseCase.CreateFrameTimeoutResult(
                MouseAction.Click,
                position,
                null,
                null);

            Assert.That(response.Action, Is.EqualTo(MouseAction.Click.ToString()));
            Assert.That(response.HitGameObjectName, Is.Null);
            Assert.That(response.EndPositionX, Is.Null);
            Assert.That(response.EndPositionY, Is.Null);
        }

        /// <summary>
        /// Verifies click results retain normal, bypass, and no-hit message contracts.
        /// </summary>
        [TestCase(false, true, "ClickTarget", "", "Clicked 'ClickTarget' at (10.3, 20.8)")]
        [TestCase(true, true, "ClickTarget", "Canvas/ClickTarget", "Bypass-clicked 'ClickTarget' at (10.3, 20.8) via 'Canvas/ClickTarget'")]
        [TestCase(false, false, null, "", "Clicked at (10.3, 20.8) - no UI element hit")]
        public void CreateClickResult_WithHitMode_MapsMessageAndFields(
            bool bypassRaycast,
            bool hitTarget,
            string? targetName,
            string targetPath,
            string expectedMessage)
        {
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Click,
                BypassRaycast = bypassRaycast,
                TargetPath = targetPath
            });
            Vector2 position = new(10.25f, 20.75f);

            SimulateMouseUiResponse response =
                SimulateMouseUiUseCase.CreateClickResult(command, position, targetName, hitTarget);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.Action, Is.EqualTo(MouseAction.Click.ToString()));
            Assert.That(response.HitGameObjectName, Is.EqualTo(targetName));
            Assert.That(response.PositionX, Is.EqualTo(position.x));
            Assert.That(response.PositionY, Is.EqualTo(position.y));
        }

        /// <summary>
        /// Verifies long-press results retain normal, bypass, and no-hit message contracts.
        /// </summary>
        [TestCase(false, true, "PressTarget", "", "Long-pressed 'PressTarget' at (10.3, 20.8) for 1.3s")]
        [TestCase(true, true, "PressTarget", "Canvas/PressTarget", "Bypass-long-pressed 'PressTarget' at (10.3, 20.8) via 'Canvas/PressTarget' for 1.3s")]
        [TestCase(false, false, null, "", "Long-pressed at (10.3, 20.8) for 1.3s - no UI element hit")]
        public void CreateLongPressResult_WithHitMode_MapsMessageAndFields(
            bool bypassRaycast,
            bool hitTarget,
            string? targetName,
            string targetPath,
            string expectedMessage)
        {
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.LongPress,
                BypassRaycast = bypassRaycast,
                TargetPath = targetPath,
                Duration = 1.25f
            });
            Vector2 position = new(10.25f, 20.75f);

            SimulateMouseUiResponse response =
                SimulateMouseUiUseCase.CreateLongPressResult(command, position, targetName, hitTarget);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.Action, Is.EqualTo(MouseAction.LongPress.ToString()));
            Assert.That(response.HitGameObjectName, Is.EqualTo(targetName));
            Assert.That(response.PositionX, Is.EqualTo(position.x));
            Assert.That(response.PositionY, Is.EqualTo(position.y));
        }

        /// <summary>
        /// Verifies one-shot drag results retain normal and bypass message contracts.
        /// </summary>
        [TestCase(false, "", "Dragged 'DragTarget' from (10.3, 20.8) to (30.5, 40.3) at 125 px/s")]
        [TestCase(true, "Canvas/DragTarget", "Bypass-dragged 'DragTarget' from (10.3, 20.8) to (30.5, 40.3) via 'Canvas/DragTarget' at 125 px/s")]
        public void CreateDragResult_WithBypassMode_MapsMessageAndFields(
            bool bypassRaycast,
            string targetPath,
            string expectedMessage)
        {
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.Drag,
                BypassRaycast = bypassRaycast,
                TargetPath = targetPath,
                DragSpeed = 125f
            });
            Vector2 startPosition = new(10.25f, 20.75f);
            Vector2 endPosition = new(30.5f, 40.25f);

            SimulateMouseUiResponse response = SimulateMouseUiUseCase.CreateDragResult(
                command,
                startPosition,
                endPosition,
                "DragTarget");

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.Action, Is.EqualTo(MouseAction.Drag.ToString()));
            Assert.That(response.HitGameObjectName, Is.EqualTo("DragTarget"));
            Assert.That(response.PositionX, Is.EqualTo(startPosition.x));
            Assert.That(response.PositionY, Is.EqualTo(startPosition.y));
            Assert.That(response.EndPositionX, Is.EqualTo(endPosition.x));
            Assert.That(response.EndPositionY, Is.EqualTo(endPosition.y));
        }

        /// <summary>
        /// Verifies drag-end results preserve the final position and speed message.
        /// </summary>
        [Test]
        public void CreateDragEndResult_WithFinalPosition_MapsMessageAndFields()
        {
            MouseUiSimulationCommand command = CreateCommand(new SimulateMouseUiSchema
            {
                Action = UnityCliLoopMouseUiAction.DragEnd,
                DragSpeed = 125f
            });
            Vector2 endPosition = new(30.5f, 40.25f);

            SimulateMouseUiResponse response = SimulateMouseUiUseCase.CreateDragEndResult(
                command,
                endPosition,
                "DragTarget");

            Assert.That(response.Success, Is.True);
            Assert.That(
                response.Message,
                Is.EqualTo("Drag ended on 'DragTarget' at (30.5, 40.3) at 125 px/s"));
            Assert.That(response.Action, Is.EqualTo(MouseAction.DragEnd.ToString()));
            Assert.That(response.HitGameObjectName, Is.EqualTo("DragTarget"));
            Assert.That(response.PositionX, Is.EqualTo(endPosition.x));
            Assert.That(response.PositionY, Is.EqualTo(endPosition.y));
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
}
