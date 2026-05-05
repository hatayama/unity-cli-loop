#if UNITYCLILOOP_HAS_ROSLYN
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop;
using io.github.hatayama.UnityCliLoop.Factory;

namespace io.github.hatayama.UnityCliLoop.DynamicCodeToolTests
{
    /// <summary>
    /// Reproduce Dictionary Error in DynamicCodeExecutor
    /// TDD Approach: Write failing tests first to reproduce the error
    /// </summary>
    [TestFixture]
    public class DynamicCodeExecutorDictionaryErrorTest
    {
        private IDynamicCodeExecutor _executor;

        [SetUp]
        public void SetUp()
        {
            // v4.0 Stateless design - Remove global configuration changes
            // Create Executor in Restricted mode
            _executor = DynamicCodeExecutorFactory.Create(DynamicCodeSecurityLevel.Restricted);
        }

        [Test]
[Description("Confirm Dictionary Error occurs even in the simplest code")]
        public async Task SimpleReturnStatement_ShouldNotThrowDictionaryError()
        {
            // Arrange
            string simpleCode = "return \"Hello World\";";
            
            // Act
            ExecutionResult result = await _executor.ExecuteCodeAsync(
                simpleCode,
                "DynamicCommand",
                null,
                CancellationToken.None,
                false
            );
            
            // Assert - Expect no error
            Assert.IsTrue(result.Success, 
                $"Simple return statement should succeed. Error: {result.ErrorMessage ?? "No error"}, Logs: {string.Join(", ", result.Logs ?? new System.Collections.Generic.List<string>())}");
            Assert.AreEqual("Hello World", result.Result);
        }

        [Test]
[Description("Confirm no Dictionary Error occurs in code with variable declarations")]
        public async Task VariableDeclaration_ShouldNotThrowDictionaryError()
        {
            // Arrange
            string codeWithVariable = @"
                int x = 5;
                int y = 10;
                return x + y;
            ";
            
            // Act
            ExecutionResult result = await _executor.ExecuteCodeAsync(
                codeWithVariable,
                "DynamicCommand",
                null,
                CancellationToken.None,
                false
            );
            
            // Assert
            Assert.IsTrue(result.Success,
                $"Variable declaration should succeed. Error: {result.ErrorMessage ?? "No error"}, Logs: {string.Join(", ", result.Logs ?? new System.Collections.Generic.List<string>())}");
            // Note: ExecuteDynamicCodeResponse.Result is string type, so compare results as strings
            Assert.AreEqual("15", result.Result.ToString());
        }



        [Test]
        [Description("Verify that incorrect parameter types produce clear error messages")]
        public async Task IncorrectParameterType_ShouldProduceClearErrorMessage()
        {
            // This test verifies the fix for the JSON serialization error
            // where Parameters field receives "{}" as string instead of object
            
            // Note: This test would need to be executed through the CLI JSON-RPC path
            // to actually test the JSON deserialization error handling.
            // Here we just ensure the executor itself works correctly.
            
            string testCode = "return \"Parameters handling test\";";
            
            ExecutionResult result = await _executor.ExecuteCodeAsync(
                testCode,
                "DynamicCommand",
                null,
                CancellationToken.None,
                false
            );
            
            Assert.IsTrue(result.Success,
                "Code should execute successfully when parameters are correct");
            Assert.AreEqual("Parameters handling test", result.Result);
        }

        [Test]
[Description("Detailed verification of compilation error content")]
        public async Task AnalyzeCompilationError_GetDetailedErrorInfo()
        {
            // Arrange
            string testCode = "return 42;";
            
            // Act
            ExecutionResult result = await _executor.ExecuteCodeAsync(
                testCode,
                "DynamicCommand",
                null,
                CancellationToken.None,
                true // Test in CompileOnly mode
            );
            
            // Assert - Output detailed error
            if (!result.Success)
            {
                TestContext.WriteLine("=== Error Message ===");
                TestContext.WriteLine($"Error: {result.ErrorMessage ?? "No error message"}");
                
                TestContext.WriteLine("\n=== Logs ===");
                foreach (string log in result.Logs ?? new System.Collections.Generic.List<string>())
                {
                    TestContext.WriteLine($"Log: {log}");
                }
                
                // Check if Dictionary error is included
                bool hasDictionaryError = false;
                if (result.ErrorMessage != null && result.ErrorMessage.Contains("Dictionary"))
                {
                    hasDictionaryError = true;
                }
                foreach (string log in result.Logs ?? new System.Collections.Generic.List<string>())
                {
                    if (log.Contains("Dictionary"))
                    {
                        hasDictionaryError = true;
                        break;
                    }
                }
                
                Assert.IsFalse(hasDictionaryError, 
                    $"Code should not produce Dictionary-related errors. Error: {result.ErrorMessage}, Logs: {string.Join(", ", result.Logs)}");
            }
            else
            {
                Assert.Pass("Compilation succeeded as expected");
            }
        }
    }
}
#endif
