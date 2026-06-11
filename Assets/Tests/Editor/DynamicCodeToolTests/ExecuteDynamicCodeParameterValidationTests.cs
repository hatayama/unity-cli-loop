using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
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
            UnityCliLoopToolExecutionService executionService = new();
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

        [Test]
        public async Task ExecuteAsync_WithObjectParameters_ShouldSucceedInCompileOnly()
        {
            // Verifies that an object Parameters value passes validation and the compile-only flow succeeds.
            // Arrange
            DynamicCodeSecurityLevel prev = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            UnityCliLoopToolExecutionService executionService = new();
            JObject paramsToken = new()            {
                ["Code"] = "return \"ok\";",
                ["Parameters"] = new JObject(), // valid: object
                ["CompileOnly"] = true
            };

            // Act
            UnityCliLoopToolResponse baseResponse = null;
            try
            {
                baseResponse = await executionService.ExecuteToolAsync(
                    registry,
                    "execute-dynamic-code",
                    paramsToken,
                    CancellationToken.None);
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(prev);
            }
            ExecuteDynamicCodeResponse response = baseResponse as ExecuteDynamicCodeResponse;

            // Assert
            Assert.IsNotNull(response, "Response should be ExecuteDynamicCodeResponse");
            Assert.IsTrue(response.Success, $"Expected success but got error: {response.ErrorMessage}");
            Assert.IsTrue(string.IsNullOrEmpty(response.ErrorMessage), "ErrorMessage should be empty on success");
        }

    }
}
