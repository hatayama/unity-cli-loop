using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Execute Dynamic Code Tool Security behavior.
    /// </summary>
    [TestFixture]
    public class ExecuteDynamicCodeToolSecurityTests
    {
        [Test]
        public async Task ExecuteAsync_Restricted_FileExists_ShouldUseCompilerSecurityRulesInsteadOfToolLocalBlock()
        {
            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            UnityCliLoopToolExecutionService executionService = new();

            try
            {
                JObject paramsToken = new()                {
                    ["Code"] = "bool exists = System.IO.File.Exists(\"dummy.txt\"); return exists;",
                    ["CompileOnly"] = false
                };

                ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);
                UnityCliLoopToolResponse response = await executionService.ExecuteToolAsync(
                    registry,
                    "execute-dynamic-code",
                    paramsToken,
                    CancellationToken.None);
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
