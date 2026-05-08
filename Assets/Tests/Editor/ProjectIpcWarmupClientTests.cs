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
            List<byte> headerBytes = HeaderBytes("Content-Length: 12\r\n\r\n");

            int contentLength = ProjectIpcWarmupClient.ParseContentLength(headerBytes);

            Assert.That(contentLength, Is.EqualTo(12));
        }

        [Test]
        public void ParseContentLength_WhenPayloadExceedsLimit_Throws()
        {
            // Tests that warmup response framing rejects payloads that would allocate too much memory.
            List<byte> headerBytes = HeaderBytes($"Content-Length: {BufferConfig.MAX_MESSAGE_SIZE + 1}\r\n\r\n");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ProjectIpcWarmupClient.ParseContentLength(headerBytes));

            Assert.That(exception.Message, Does.Contain("invalid Content-Length"));
        }

        private static List<byte> HeaderBytes(string header)
        {
            return new List<byte>(Encoding.ASCII.GetBytes(header));
        }
    }
}
