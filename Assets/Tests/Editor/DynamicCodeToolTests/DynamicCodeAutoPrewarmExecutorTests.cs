using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class DynamicCodeAutoPrewarmExecutorTests
    {
        [Test]
        public async Task ExecuteAsync_WhenTransportReturnsSuccessfulResult_ShouldReturnSuccess()
        {
            DynamicCodeAutoPrewarmExecutor executor = new DynamicCodeAutoPrewarmExecutor(
                (requestJson, ct) => Task.FromResult(
                    "{\"jsonrpc\":\"2.0\",\"id\":\"dynamic-code-auto-prewarm\",\"result\":{\"success\":true}}"));

            DynamicCodeAutoPrewarmResult result = await executor.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ErrorMessage, Is.Null.Or.EqualTo(string.Empty));
        }

        [Test]
        public async Task ExecuteAsync_WhenTransportReturnsJsonRpcError_ShouldReturnFailure()
        {
            DynamicCodeAutoPrewarmExecutor executor = new DynamicCodeAutoPrewarmExecutor(
                (requestJson, ct) => Task.FromResult(
                    "{\"jsonrpc\":\"2.0\",\"id\":\"dynamic-code-auto-prewarm\",\"error\":{\"message\":\"server session changed\"}}"));

            DynamicCodeAutoPrewarmResult result = await executor.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("server session changed"));
        }

        [Test]
        public async Task ExecuteAsync_WhenTransportThrowsIOException_ShouldReturnTransportFailure()
        {
            DynamicCodeAutoPrewarmExecutor executor = new DynamicCodeAutoPrewarmExecutor(
                (requestJson, ct) => Task.FromException<string>(new IOException("socket closed")));

            DynamicCodeAutoPrewarmResult result = await executor.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("dynamic code auto prewarm transport failed"));
        }

        [Test]
        public void ExecuteAsync_WhenTransportThrowsInvalidOperationException_ShouldFailFast()
        {
            DynamicCodeAutoPrewarmExecutor executor = new DynamicCodeAutoPrewarmExecutor(
                (requestJson, ct) => Task.FromException<string>(new InvalidOperationException("response stream closed before a full frame arrived")));

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await executor.ExecuteAsync(
                    new ExecuteDynamicCodeSchema { Code = "return 1;" },
                    CancellationToken.None));
        }

        [Test]
        public async Task ExecuteAsync_WhenTransportIgnoresCancellation_ShouldReturnTimeout()
        {
            DynamicCodeAutoPrewarmExecutor executor = new DynamicCodeAutoPrewarmExecutor(
                (requestJson, ct) => new TaskCompletionSource<string>().Task,
                timeoutMilliseconds: 1);

            DynamicCodeAutoPrewarmResult result = await executor.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(DynamicCodeAutoPrewarmExecutor.TimeoutErrorMessage));
        }
    }
}
