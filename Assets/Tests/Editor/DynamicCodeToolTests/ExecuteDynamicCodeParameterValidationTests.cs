#if UNITYCLILOOP_HAS_ROSLYN
using NUnit.Framework;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
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
            // Arrange
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            JObject paramsToken = new()            {
                ["Code"] = "return \"ok\";",
                ["Parameters"] = "{}", // invalid: string instead of object
                ["CompileOnly"] = true
            };

            // Act & Assert
            UnityCliLoopToolParameterValidationException ex =
                Assert.ThrowsAsync<UnityCliLoopToolParameterValidationException>(async () =>
            {
                await registry.ExecuteToolAsync("execute-dynamic-code", paramsToken);
            });

            Assert.IsNotNull(ex);
            StringAssert.Contains("Parameter 'Parameters' must be an object, not a string.", ex.Message);
            StringAssert.Contains("{}", ex.Message);
        }

        [Test]
        public async Task ExecuteAsync_WithObjectParameters_ShouldSucceedInCompileOnly()
        {
            // Arrange
            DynamicCodeSecurityLevel prev = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            JObject paramsToken = new()            {
                ["Code"] = "return \"ok\";",
                ["Parameters"] = new JObject(), // valid: object
                ["CompileOnly"] = true
            };

            // Act
            UnityCliLoopToolResponse baseResponse = null;
            try
            {
                baseResponse = await registry.ExecuteToolAsync("execute-dynamic-code", paramsToken);
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

        [Test]
        public async Task ExecuteAsync_CodeWithoutReturn_ShouldAutoReturnAndSucceed()
        {
            // Arrange
            DynamicCodeSecurityLevel prev = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            JObject paramsToken = new()            {
                ["Code"] = "int x = 1; // no explicit return",
                ["CompileOnly"] = false
            };

            // Act
            UnityCliLoopToolResponse baseResponse = null;
            try
            {
                baseResponse = await registry.ExecuteToolAsync("execute-dynamic-code", paramsToken);
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
#endif

