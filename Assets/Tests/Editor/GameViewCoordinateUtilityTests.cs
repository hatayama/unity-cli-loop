using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class GameViewCoordinateUtilityTests
    {
        [Test]
        public void ConvertInputToUnity_WhenInputIsNearTop_ShouldFlipYWithinGameView()
        {
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
            Vector2 gameViewSize = new Vector2(1920f, 1080f);
            Vector2 inputPosition = new Vector2(960f, 540f);

            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);

            Assert.That(conversion.InjectedUnityPosition, Is.EqualTo(new Vector2(960f, 540f)));
        }

        [Test]
        public void ConvertInputToUnity_WhenInputIsAtBottomRight_ShouldMapToBottomLeftOrigin()
        {
            Vector2 gameViewSize = new Vector2(1920f, 1080f);
            Vector2 inputPosition = new Vector2(1920f, 1080f);

            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);

            Assert.That(conversion.InjectedUnityPosition, Is.EqualTo(new Vector2(1920f, 0f)));
        }
    }
}
