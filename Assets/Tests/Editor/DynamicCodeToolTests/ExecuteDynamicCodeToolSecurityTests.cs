using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class ExecuteDynamicCodeToolSecurityTests
    {
        [Test]
        public async Task ExecuteAsync_Restricted_FileExists_ShouldUseCompilerSecurityRulesInsteadOfToolLocalBlock()
        {
            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ExecuteDynamicCodeTool tool = new ExecuteDynamicCodeTool();

            try
            {
                JObject paramsToken = new JObject
                {
                    ["Code"] = "bool exists = System.IO.File.Exists(\"dummy.txt\"); return exists;",
                    ["CompileOnly"] = false
                };

                ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
                BaseToolResponse response = await tool.ExecuteAsync(paramsToken);
                ExecuteDynamicCodeResponse typedResponse = response as ExecuteDynamicCodeResponse;

                Assert.IsNotNull(typedResponse, "Response should be ExecuteDynamicCodeResponse");
                Assert.IsTrue(typedResponse.Success, $"Tool should allow safe File.Exists through centralized security validation. Error: {typedResponse.ErrorMessage}");
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }
    }
}
