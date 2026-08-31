using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests for PlayModeTestExecuter
    /// </summary>
    [TestFixture]
    public class PlayModeTestExecuterTests
    {
        /// <summary>
        /// Test that when filter is null, a filter for all tests is generated
        /// </summary>
        [Test]
        public void CreateUnityFilter_WithNullFilter_ShouldCreateAllTestsFilter()
        {
            // Act
            System.Reflection.MethodInfo createUnityFilterMethod = typeof(PlayModeTestExecuter)
                .GetMethod("CreateUnityFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (createUnityFilterMethod == null)
            {
                Assert.Fail("CreateUnityFilter method not found");
                return;
            }
            
            Filter unityFilter = (Filter)createUnityFilterMethod.Invoke(null, new object[] { TestMode.PlayMode, null });
            
            // Assert
            Assert.That(unityFilter, Is.Not.Null);
            Assert.That(unityFilter.testMode, Is.EqualTo(TestMode.PlayMode));
            
            // No other filters are set
            Assert.That(unityFilter.testNames, Is.Null);
            Assert.That(unityFilter.groupNames, Is.Null);
        }
        
        /// <summary>
        /// Test that regex filter works correctly
        /// </summary>
        [Test]
        public void CreateUnityFilter_WithRegexFilter_ShouldCreateCorrectFilter()
        {
            // Arrange
            string regexPattern = "Test.*Method";
            TestExecutionFilter filter = TestExecutionFilter.ByClassName(regexPattern);
            
            // Act
            System.Reflection.MethodInfo createUnityFilterMethod = typeof(PlayModeTestExecuter)
                .GetMethod("CreateUnityFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (createUnityFilterMethod == null)
            {
                Assert.Fail("CreateUnityFilter method not found");
                return;
            }
            // Act
            Filter unityFilter = (Filter)createUnityFilterMethod.Invoke(
                null,
                new object[] { TestMode.PlayMode, filter }
            );

            // Assert
            Assert.That(unityFilter, Is.Not.Null);
            Assert.That(unityFilter.testMode, Is.EqualTo(TestMode.PlayMode));
            // Depending on your implementation choice, verify groupNames:
            // 1) Raw pattern passed through:
            Assert.That(unityFilter.groupNames, Is.EquivalentTo(new[] { regexPattern }));
            // 2) If you anchor–and–escape internally, comment out 1) above and use this:
            // var expected = "^" + System.Text.RegularExpressions.Regex.Escape(regexPattern) + "(\\.|$)";
            // Assert.That(unityFilter.groupNames, Is.EquivalentTo(new[] { expected }));
        }
    }
}