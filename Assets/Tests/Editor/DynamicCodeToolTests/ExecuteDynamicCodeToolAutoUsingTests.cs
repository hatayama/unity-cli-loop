using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.DynamicCodeToolTests
{
    [TestFixture]
    public class ExecuteDynamicCodeToolAutoUsingTests
    {
        [Test]
        public async Task ExecuteAsync_CompileOnly_WhenTypeRequiresMissingUsing_ShouldSucceed()
        {
            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ExecuteDynamicCodeTool tool = new ExecuteDynamicCodeTool();

            try
            {
                JObject paramsToken = new JObject
                {
                    ["Code"] = "StringBuilder builder = new StringBuilder(); builder.Append(\"ok\"); return builder.ToString();",
                    ["CompileOnly"] = true
                };

                ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
                BaseToolResponse response = await tool.ExecuteAsync(paramsToken);
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
