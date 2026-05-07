using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Basic PlayMode tests to verify test framework functionality
    /// </summary>
    public class BasicPlayModeTests
    {
        [Test]
        public void BasicTest_ShouldPass()
        {
            // Arrange
            int expected = 5;
            int actual = 2 + 3;
            
            // Act & Assert
            Assert.AreEqual(expected, actual, "Basic math should work correctly");
        }

        [Test]
        public void StringTest_ShouldPass()
        {
            // Arrange
            string expected = "Hello World";
            string actual = "Hello" + " " + "World";
            
            // Act & Assert
            Assert.AreEqual(expected, actual, "String concatenation should work");
        }

        [UnityTest]
        public IEnumerator FrameTest_ShouldPassAfterWaiting()
        {
            // Arrange
            float startTime = Time.time;
            
            // Act - Wait for one frame
            yield return null;
            
            // Assert
            float endTime = Time.time;
            Assert.GreaterOrEqual(endTime, startTime, "Time should progress during frame wait");
        }

        [Test]
        public void UnityEngineTest_ShouldPass()
        {
            // Test Unity engine functionality
            Vector3 vector = new(1, 2, 3);
            float magnitude = vector.magnitude;
            
            Assert.Greater(magnitude, 0, "Vector magnitude should be positive");
            Assert.AreEqual(Mathf.Sqrt(1 + 4 + 9), magnitude, 0.001f, "Vector magnitude calculation should be correct");
        }

        [Test]
        public void ApplicationTest_ShouldPass()
        {
            // Test Application properties that are available in PlayMode
            Assert.IsTrue(UnityEngine.Application.isPlaying, "Application should be in play mode during PlayMode tests");
            Assert.IsNotNull(UnityEngine.Application.unityVersion, "Unity version should be available");
        }
    }
}