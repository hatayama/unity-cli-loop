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
    }
}
