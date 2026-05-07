using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Execute Dynamic Code Tool Auto Using behavior.
    /// </summary>
    [TestFixture]
    public class ExecuteDynamicCodeToolAutoUsingTests
    {
        [Test]
        public async Task ExecuteAsync_CompileOnly_WhenTypeRequiresMissingUsing_ShouldSucceed()
        {
            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            try
            {
                JObject paramsToken = new()                {
                    ["Code"] = "StringBuilder builder = new StringBuilder(); builder.Append(\"ok\"); return builder.ToString();",
                    ["CompileOnly"] = true
                };

                ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
                UnityCliLoopToolResponse response = await registry.ExecuteToolAsync("execute-dynamic-code", paramsToken);
                ExecuteDynamicCodeResponse typedResponse = response as ExecuteDynamicCodeResponse;

                Assert.IsNotNull(typedResponse, "Response should be ExecuteDynamicCodeResponse");
                Assert.IsTrue(typedResponse.Success, $"Tool should compile after injecting missing using directives. Error: {typedResponse.ErrorMessage}");
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }
    }
}
