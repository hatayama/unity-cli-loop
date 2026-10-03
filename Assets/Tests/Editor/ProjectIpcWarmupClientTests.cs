using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    [TestFixture]
    public sealed class ProjectIpcWarmupClientTests
    {
        [Test]
        public void ParseContentLength_WhenPayloadIsWithinLimit_ReturnsLength()
        {
            // Tests that warmup response framing accepts payloads within the shared IPC size limit.
            ProjectIpcWarmupClient client = new();
            List<byte> headerBytes = HeaderBytes("Content-Length: 12\r\n\r\n");

            int contentLength = client.ParseContentLength(headerBytes);

            Assert.That(contentLength, Is.EqualTo(12));
        }

        [Test]
        public void ParseContentLength_WhenPayloadExceedsLimit_Throws()
        {
            // Tests that warmup response framing rejects payloads that would allocate too much memory.
            ProjectIpcWarmupClient client = new();
            List<byte> headerBytes = HeaderBytes($"Content-Length: {BufferConfig.MAX_MESSAGE_SIZE + 1}\r\n\r\n");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => client.ParseContentLength(headerBytes));

            Assert.That(exception.Message, Does.Contain("invalid Content-Length"));
        }

        [Test]
        public void ValidateJsonRpcSuccessResponse_WhenResponseContainsError_Throws()
        {
            // Tests that warmup response validation rejects server-side JSON-RPC errors.
            ProjectIpcWarmupClient client = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => client.ValidateJsonRpcSuccessResponse(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32603,\"message\":\"The installed uloop CLI uses an IPC protocol that does not match this Unity package.\"}}"));

            Assert.That(exception.Message, Does.Contain("does not match"));
        }

        [Test]
        public void ValidateJsonRpcSuccessResponse_WhenResponseContainsResult_DoesNotThrow()
        {
            // Tests that warmup response validation accepts successful JSON-RPC responses.
            ProjectIpcWarmupClient client = new();

            client.ValidateJsonRpcSuccessResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}");
        }

        private static List<byte> HeaderBytes(string header)
        {
            return new List<byte>(Encoding.ASCII.GetBytes(header));
        }

        /// <summary>
        /// Verifies header lines other than Content-Length are skipped before the length is read.
        /// </summary>
        [Test]
        public void ParseContentLength_WhenOtherHeaderPrecedesContentLength_ReturnsLength()
        {
            ProjectIpcWarmupClient client = new();
            List<byte> headerBytes = HeaderBytes("Content-Type: application/json\r\nContent-Length: 7\r\n\r\n");

            int contentLength = client.ParseContentLength(headerBytes);

            Assert.That(contentLength, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies a header block without Content-Length is rejected with a missing-header message.
        /// </summary>
        [Test]
        public void ParseContentLength_WhenContentLengthIsMissing_ThrowsMissingHeader()
        {
            ProjectIpcWarmupClient client = new();
            List<byte> headerBytes = HeaderBytes("Content-Type: application/json\r\n\r\n");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => client.ParseContentLength(headerBytes));

            Assert.That(exception.Message, Does.Contain("did not include Content-Length"));
        }

        /// <summary>
        /// Verifies a JSON-RPC error without a message is reported using the serialized error object.
        /// </summary>
        [Test]
        public void ValidateJsonRpcSuccessResponse_WhenErrorHasNoMessage_ThrowsWithErrorObjectText()
        {
            ProjectIpcWarmupClient client = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => client.ValidateJsonRpcSuccessResponse(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32603}}"));

            Assert.That(exception.Message, Does.StartWith("Project IPC warmup returned JSON-RPC error: "));
            Assert.That(exception.Message, Does.Contain("-32603"));
        }

        /// <summary>
        /// Verifies a response with neither error nor a non-null result is rejected.
        /// </summary>
        [TestCase("{\"jsonrpc\":\"2.0\",\"id\":1}")]
        [TestCase("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":null}")]
        public void ValidateJsonRpcSuccessResponse_WhenResultIsMissingOrNull_ThrowsMissingResult(string responseJson)
        {
            ProjectIpcWarmupClient client = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => client.ValidateJsonRpcSuccessResponse(responseJson));

            Assert.That(exception.Message, Does.Contain("did not include a JSON-RPC result"));
        }
    }
}
