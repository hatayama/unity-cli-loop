using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the warmup request and response exchange over an already connected stream: the request frame it
    /// writes, and how it fails when the response headers or payload end early or the headers grow too large. An
    /// in-memory stream that completes every read and write synchronously stands in for the socket or pipe.
    /// </summary>
    public sealed class ProjectIpcWarmupClientExchangeTests
    {
        private const string RequestJson = "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"id\":1}";

        /// <summary>
        /// Verifies a successful JSON-RPC response completes the exchange after the request was written as one
        /// Content-Length frame.
        /// </summary>
        [Test]
        public async Task ExchangeFrameAsync_WithASuccessResponse_WritesTheRequestFrameAndCompletes()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Frame("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"));

            await new ProjectIpcWarmupClient().ExchangeFrameAsync(stream, RequestJson, CancellationToken.None);

            Assert.That(stream.WrittenText, Is.EqualTo("Content-Length: " + Encoding.UTF8.GetByteCount(RequestJson) + "\r\n\r\n" + RequestJson));
        }

        /// <summary>
        /// Verifies a JSON-RPC error response fails the exchange.
        /// </summary>
        [Test]
        public async Task ExchangeFrameAsync_WithAnErrorResponse_Throws()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(
                Frame("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-1,\"message\":\"not ready\"}}"));

            Exception exception = await CaptureExceptionAsync(
                () => new ProjectIpcWarmupClient().ExchangeFrameAsync(stream, RequestJson, CancellationToken.None));

            Assert.That(exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(exception.Message, Does.Contain("not ready"));
        }

        /// <summary>
        /// Verifies a stream that ends before the blank line closing the headers fails as an early end of stream.
        /// </summary>
        [Test]
        public async Task ExchangeFrameAsync_WhenTheHeadersEndEarly_ThrowsEndOfStream()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Encoding.ASCII.GetBytes("Content-Length: 2\r\n"));

            Exception exception = await CaptureExceptionAsync(
                () => new ProjectIpcWarmupClient().ExchangeFrameAsync(stream, RequestJson, CancellationToken.None));

            Assert.That(exception, Is.InstanceOf<EndOfStreamException>());
            Assert.That(exception.Message, Does.Contain("response headers"));
        }

        /// <summary>
        /// Verifies headers longer than the 8192-byte limit are rejected instead of being read without end.
        /// </summary>
        [Test]
        public async Task ExchangeFrameAsync_WhenTheHeadersExceedTheLimit_Throws()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Encoding.ASCII.GetBytes(new string('a', 8193)));

            Exception exception = await CaptureExceptionAsync(
                () => new ProjectIpcWarmupClient().ExchangeFrameAsync(stream, RequestJson, CancellationToken.None));

            Assert.That(exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(exception.Message, Does.Contain("exceeded the maximum size"));
        }

        /// <summary>
        /// Verifies headers of exactly 8192 bytes are still accepted.
        /// </summary>
        [Test]
        public async Task ExchangeFrameAsync_WhenTheHeadersAreExactlyAtTheLimit_Completes()
        {
            string json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}";
            string contentLength = "Content-Length: " + json.Length + "\r\n";
            string padding = "X-Pad: " + new string('a', 8192 - contentLength.Length - "X-Pad: ".Length - 4) + "\r\n";
            byte[] headers = Encoding.ASCII.GetBytes(contentLength + padding + "\r\n");
            Assume.That(headers.Length, Is.EqualTo(8192));
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Concat(headers, Encoding.UTF8.GetBytes(json)));

            await new ProjectIpcWarmupClient().ExchangeFrameAsync(stream, RequestJson, CancellationToken.None);

            Assert.That(stream.RemainingByteCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a stream that ends before the announced payload length fails as an early end of stream.
        /// </summary>
        [Test]
        public async Task ExchangeFrameAsync_WhenThePayloadEndsEarly_ThrowsEndOfStream()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Encoding.ASCII.GetBytes("Content-Length: 50\r\n\r\n{}"));

            Exception exception = await CaptureExceptionAsync(
                () => new ProjectIpcWarmupClient().ExchangeFrameAsync(stream, RequestJson, CancellationToken.None));

            Assert.That(exception, Is.InstanceOf<EndOfStreamException>());
            Assert.That(exception.Message, Does.Contain("response payload"));
        }

        private static async Task<Exception> CaptureExceptionAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                return exception;
            }

            Assert.Fail("The exchange was expected to fail.");
            return null;
        }

        private static byte[] Frame(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            return Concat(Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n"), body);
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            byte[] combined = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, combined, 0, first.Length);
            Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
            return combined;
        }

        /// <summary>
        /// A connected stream that hands out its scripted bytes, then reports the end of the stream, and records what
        /// is written to it. Reading before anything was written fails, like a server that never answers an unsent
        /// request. Reads and writes complete synchronously so no thread pool work is started.
        /// </summary>
        private sealed class ScriptedDuplexStream : Stream
        {
            private readonly byte[] _incoming;
            private readonly MemoryStream _written = new MemoryStream();
            private int _readPosition;

            public ScriptedDuplexStream(byte[] incoming)
            {
                _incoming = incoming;
            }

            public string WrittenText => Encoding.UTF8.GetString(_written.ToArray());

            public int RemainingByteCount => _incoming.Length - _readPosition;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // A real server answers only after it has received the request, so reading first would wait forever.
                if (_written.Length == 0)
                {
                    throw new InvalidOperationException("The response was read before the request was written.");
                }

                int available = Math.Min(count, _incoming.Length - _readPosition);
                Buffer.BlockCopy(_incoming, _readPosition, buffer, offset, available);
                _readPosition += available;
                return available;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.FromResult(Read(buffer, offset, count));
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _written.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }
        }
    }
}
