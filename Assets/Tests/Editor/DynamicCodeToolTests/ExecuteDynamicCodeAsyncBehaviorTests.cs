#if ULOOPMCP_HAS_ROSLYN
using NUnit.Framework;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    public class ExecuteDynamicCodeAsyncBehaviorTests
    {
        [Test]
        public async Task Await_TaskCompletionSource_Result_Propagates()
        {
            IDynamicCodeExecutor executor = Factory.DynamicCodeExecutorFactory.Create(
                DynamicCodeSecurityLevel.Restricted
            );

            string code = @"var tcs = (System.Threading.Tasks.TaskCompletionSource<object>)parameters[""param0""]; var r = await tcs.Task; return r;";

            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            Task<ExecutionResult> running = executor.ExecuteCodeAsync(
                code,
                "DynamicCommand",
                new object[] { tcs },
                CancellationToken.None,
                false
            );

            Assert.IsFalse(running.IsCompleted, "Execution should be awaiting TaskCompletionSource");
            tcs.SetResult("OK");

            ExecutionResult res = await running;
            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.AreEqual("OK", res.Result);
        }

        [Test]
        public async Task Await_TaskCompletionSource_Exception_Propagates()
        {
            IDynamicCodeExecutor executor = Factory.DynamicCodeExecutorFactory.Create(
                DynamicCodeSecurityLevel.Restricted
            );

            string code = @"var tcs = (System.Threading.Tasks.TaskCompletionSource<object>)parameters[""param0""]; var r = await tcs.Task; return r;";

            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            Task<ExecutionResult> running = executor.ExecuteCodeAsync(
                code,
                "DynamicCommand",
                new object[] { tcs },
                CancellationToken.None,
                false
            );

            tcs.SetException(new System.InvalidOperationException("boom"));
            ExecutionResult res = await running;
            Assert.IsFalse(res.Success);
            StringAssert.Contains("boom", res.ErrorMessage ?? string.Empty);
        }

        [Test]
        public async Task Await_ValueTask_ImmediateCompletion_ReturnsResult()
        {
            IDynamicCodeExecutor executor = Factory.DynamicCodeExecutorFactory.Create(
                DynamicCodeSecurityLevel.Restricted
            );

            string code = @"var vt = new System.Threading.Tasks.ValueTask<int>(456); return await vt;";

            ExecutionResult res = await executor.ExecuteCodeAsync(
                code,
                "DynamicCommand",
                null,
                CancellationToken.None,
                false
            );

            Assert.IsTrue(res.Success, res.ErrorMessage);
            Assert.AreEqual("456", res.Result?.ToString());
        }

        [Test]
        public async Task Cancellation_Token_AbortsLongDelay()
        {
            IDynamicCodeExecutor executor = Factory.DynamicCodeExecutorFactory.Create(
                DynamicCodeSecurityLevel.Restricted
            );

            using CancellationTokenSource cts = new CancellationTokenSource();

            string code = @"await System.Threading.Tasks.Task.Delay(-1, ct); return null;";

            Task<ExecutionResult> running = executor.ExecuteCodeAsync(
                code,
                "DynamicCommand",
                null,
                cts.Token,
                false
            );

            cts.Cancel();
            ExecutionResult res = await running;
            Assert.IsFalse(res.Success);
        }

        [Test]
        public async Task ExecuteCodeAsync_WhenOnlyLiteralValuesDiffer_ShouldKeepExecutionValues()
        {
            IDynamicCodeExecutor executor = Factory.DynamicCodeExecutorFactory.Create(
                DynamicCodeSecurityLevel.Restricted
            );

            ExecutionResult first = await executor.ExecuteCodeAsync(
                "int benchNonce = 100; return benchNonce;",
                "DynamicCommand",
                null,
                CancellationToken.None,
                false
            );
            ExecutionResult second = await executor.ExecuteCodeAsync(
                "int benchNonce = 200; return benchNonce;",
                "DynamicCommand",
                null,
                CancellationToken.None,
                false
            );

            Assert.IsTrue(first.Success, first.ErrorMessage);
            Assert.IsTrue(second.Success, second.ErrorMessage);
            Assert.AreEqual("100", first.Result?.ToString());
            Assert.AreEqual("200", second.Result?.ToString());
        }

    }
}
#endif
