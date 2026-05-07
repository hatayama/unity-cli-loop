using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Command Runner behavior.
    /// </summary>
    [TestFixture]
    public class CommandRunnerTests
    {
        [Test]
        public async Task ExecuteAsync_WhenCallerCancellationIsRequested_ShouldReturnNeutralCancelledMessage()
        {
            CommandRunner runner = new();
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext context = new()            {
                CompiledAssembly = typeof(global::io.github.hatayama.UnityCliLoop.Tests.Editor.Dynamic.DynamicCommand).Assembly,
                Parameters = new Dictionary<string, object>(),
                CancellationToken = cancellationTokenSource.Token
            };

            ExecutionResult result = await runner.ExecuteAsync(context);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
            Assert.That(result.Logs, Contains.Item("Execution cancelled"));
            Assert.That(result.Logs, Has.No.Member("Execution cancelled due to timeout"));
        }

        [Test]
        public async Task ExecuteAsync_WhenSyncFallbackAcceptsCancellationToken_ShouldUseSupportedSignature()
        {
            CommandRunner runner = new();

            io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext context = new()            {
                CompiledAssembly = typeof(global::io.github.hatayama.UnityCliLoop.Tests.Editor.Dynamic.DynamicCommand).Assembly,
                Parameters = new Dictionary<string, object>(),
                CancellationToken = CancellationToken.None
            };

            ExecutionResult result = await runner.ExecuteAsync(context);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo("dictionary-and-cancellation"));
        }
    }
}
