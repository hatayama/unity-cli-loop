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
    }
}
