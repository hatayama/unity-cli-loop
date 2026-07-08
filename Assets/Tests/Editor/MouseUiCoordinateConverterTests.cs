using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterizes coordinate conversion shared by mouse UI action executors.
    /// </summary>
    [TestFixture]
    public sealed class MouseUiCoordinateConverterTests
    {
        /// <summary>
        /// Verifies top-left input coordinates are flipped against the current Game view height.
        /// </summary>
        [Test]
        public void InputToScreen_WithTopLeftCoordinates_FlipsYAgainstCurrentGameViewHeight()
        {
            Vector2 inputPosition = new(12.5f, 37.25f);
            float gameViewHeight = Handles.GetMainGameViewSize().y;

            Vector2 screenPosition = SimulateMouseUiUseCase.InputToScreen(inputPosition);

            Assert.That(
                screenPosition,
                Is.EqualTo(new Vector2(inputPosition.x, gameViewHeight - inputPosition.y)));
        }

        /// <summary>
        /// Verifies converting input coordinates to screen space and back preserves the position.
        /// </summary>
        [Test]
        public void ScreenToInput_AfterInputToScreen_RestoresPosition()
        {
            Vector2 inputPosition = new(42.5f, 64.25f);

            Vector2 screenPosition = SimulateMouseUiUseCase.InputToScreen(inputPosition);
            Vector2 restoredPosition = SimulateMouseUiUseCase.ScreenToInput(screenPosition);

            Assert.That(restoredPosition, Is.EqualTo(inputPosition));
        }
    }
}
