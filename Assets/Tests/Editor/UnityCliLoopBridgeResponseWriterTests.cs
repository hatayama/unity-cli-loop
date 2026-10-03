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
    /// Tests the byte-level Content-Length framing emitted by the project IPC server.
    /// </summary>
    public sealed class UnityCliLoopBridgeResponseWriterTests
    {
        /// <summary>
        /// Verifies an empty JSON response does not emit a frame.
        /// </summary>
        [Test]
        public void CreateContentLengthFrame_WhenJsonIsEmpty_ReturnsEmptyFrame()
        {
            string frame = UnityCliLoopBridgeResponseWriter.CreateContentLengthFrame(string.Empty);

            Assert.That(frame, Is.Empty);
        }

        /// <summary>
        /// Verifies ASCII JSON is framed with its exact UTF-8 byte length and header separators.
        /// </summary>
        [Test]
        public void CreateContentLengthFrame_WhenJsonIsAscii_ReturnsExactFrame()
        {
            const string Json = "{\"id\":1}";

            string frame = UnityCliLoopBridgeResponseWriter.CreateContentLengthFrame(Json);

            Assert.That(frame, Is.EqualTo("Content-Length: 8\r\n\r\n{\"id\":1}"));
        }

        /// <summary>
        /// Verifies multibyte JSON uses UTF-8 byte length rather than UTF-16 character count.
        /// </summary>
        [Test]
        public void CreateContentLengthFrame_WhenJsonContainsMultibyteText_UsesUtf8ByteLength()
        {
            const string Json = "{\"message\":\"あ\"}";

            string frame = UnityCliLoopBridgeResponseWriter.CreateContentLengthFrame(Json);

            Assert.That(frame, Is.EqualTo("Content-Length: 17\r\n\r\n{\"message\":\"あ\"}"));
        }

        /// <summary>
        /// Verifies a locked write emits the exact Content-Length frame bytes and releases the lock.
        /// </summary>
        [Test]
        public void WriteJsonResponseLockedAsync_WhenStreamIsWritable_WritesFrameAndReleasesLock()
        {
            using MemoryStream stream = new MemoryStream();
            using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

            Task writeTask = UnityCliLoopBridgeResponseWriter.WriteJsonResponseLockedAsync(
                stream,
                writeLock,
                "{\"id\":1}",
                CancellationToken.None);

            Assert.That(writeTask.IsCompleted, Is.True);
            writeTask.GetAwaiter().GetResult();
            Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Is.EqualTo("Content-Length: 8\r\n\r\n{\"id\":1}"));
            Assert.That(writeLock.CurrentCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a stream that no longer accepts writes is skipped without throwing.
        /// </summary>
        [Test]
        public void WriteJsonResponseLockedAsync_WhenStreamIsNotWritable_SkipsWrite()
        {
            byte[] backingBytes = new byte[64];
            using MemoryStream stream = new MemoryStream(backingBytes, false);
            using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

            Task writeTask = UnityCliLoopBridgeResponseWriter.WriteJsonResponseLockedAsync(
                stream,
                writeLock,
                "{\"id\":1}",
                CancellationToken.None);

            Assert.That(writeTask.IsCompleted, Is.True);
            writeTask.GetAwaiter().GetResult();
            Assert.That(backingBytes, Is.All.EqualTo((byte)0));
            Assert.That(writeLock.CurrentCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a failing stream write surfaces the failure and still releases the lock for later frames.
        /// </summary>
        [Test]
        public void WriteJsonResponseLockedAsync_WhenStreamWriteFails_FaultsAndReleasesLock()
        {
            using FailingWriteStream stream = new FailingWriteStream();
            using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

            Task writeTask = UnityCliLoopBridgeResponseWriter.WriteJsonResponseLockedAsync(
                stream,
                writeLock,
                "{\"id\":1}",
                CancellationToken.None);

            Assert.That(writeTask.IsCompleted, Is.True);
            Assert.Throws<IOException>(() => writeTask.GetAwaiter().GetResult());
            Assert.That(writeLock.CurrentCount, Is.EqualTo(1));
        }

        private sealed class FailingWriteStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;

            public override long Position
            {
                get => 0;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new IOException("write failed");
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.FromException(new IOException("write failed"));
            }
        }
    }
}
