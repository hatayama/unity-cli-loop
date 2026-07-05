using NUnit.Framework;
using System.Threading;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Tests.Editor;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Parameter validation tests for ExecuteDynamicCodeTool
    /// </summary>
    [TestFixture]
    public class ExecuteDynamicCodeParameterValidationTests
    {
        [Test]
        public void ExecuteAsync_WithStringParameters_ShouldThrowUnityCliLoopToolParameterValidationException()
        {
            // Verifies that a string Parameters value is rejected with a clear validation error before any compilation starts.
            // Arrange
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            UnityCliLoopToolExecutionService executionService = new(new NoOpEditorRuntimeStatePort());
            JObject paramsToken = new()            {
                ["Code"] = "return \"ok\";",
                ["Parameters"] = "{}", // invalid: string instead of object
                ["CompileOnly"] = true
            };

            // Act & Assert
            UnityCliLoopToolParameterValidationException ex =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(async () =>
            {
                await executionService.ExecuteToolAsync(
                    registry,
                    "execute-dynamic-code",
                    paramsToken,
                    CancellationToken.None);
            });

            Assert.IsNotNull(ex);
            StringAssert.Contains("Parameter 'Parameters' must be an object, not a string.", ex.Message);
            StringAssert.Contains("{}", ex.Message);
        }

    }
}
