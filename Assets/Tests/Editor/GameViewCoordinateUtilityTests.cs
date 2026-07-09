using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Game View Coordinate Utility behavior.
    /// </summary>
    public class GameViewCoordinateUtilityTests
    {
        [Test]
        public void ConvertInputToUnity_WhenInputIsNearTop_ShouldFlipYWithinGameView()
        {
            // Tests that a coordinate near the top-left origin flips to a high Unity Y within the Game View.
            Vector2 gameViewSize = new Vector2(1920f, 1080f);
            Vector2 inputPosition = new Vector2(0f, 100f);

            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);

            Assert.That(conversion.InputPosition, Is.EqualTo(inputPosition));
            Assert.That(conversion.InjectedUnityPosition, Is.EqualTo(new Vector2(0f, 980f)));
            Assert.That(conversion.GameViewSize, Is.EqualTo(gameViewSize));
        }

        [Test]
        public void ConvertInputToUnity_WhenInputIsAtCenter_ShouldKeepCenterY()
        {
            // Tests that a coordinate at the Game View center converts to the same center Y in Unity space.
            Vector2 gameViewSize = new Vector2(1920f, 1080f);
            Vector2 inputPosition = new Vector2(960f, 540f);

            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);

            Assert.That(conversion.InjectedUnityPosition, Is.EqualTo(new Vector2(960f, 540f)));
        }

        [Test]
        public void ConvertInputToUnity_WhenInputIsAtBottomRight_ShouldMapToBottomLeftOrigin()
        {
            // Tests that the top-left Game View bottom-right corner maps to Unity's bottom-left Y origin.
            Vector2 gameViewSize = new Vector2(1920f, 1080f);
            Vector2 inputPosition = new Vector2(1920f, 1080f);

            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);

            Assert.That(conversion.InjectedUnityPosition, Is.EqualTo(new Vector2(1920f, 0f)));
        }
    }
}
