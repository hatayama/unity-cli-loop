using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Encoding contract of the resident-worker line protocol: request lines, response headers,
    /// and the length-prefixed diagnostics payload.
    /// </summary>
    public class TransformWorkerServeProtocolTests
    {
        /// <summary>
        /// What: a request line round-trips paths containing spaces, quotes, and non-ASCII characters
        /// because both paths travel base64-encoded.
        /// </summary>
        [Test]
        public void RequestLine_RoundTripsPathsWithSpacesAndUnicode()
        {
            string inputPath = "/tmp/uloop hot reload/入力 \"quoted\".json";
            string outputPath = "C:\\Temp\\uloop\\出力.json";

            string line = TransformWorkerServeProtocol.EncodeRequestLine(inputPath, outputPath);
            bool decoded = TransformWorkerServeProtocol.TryDecodeRequestLine(line, out string decodedInput, out string decodedOutput);

            Assert.That(line.Split(' ').Length, Is.EqualTo(3), "The request must stay a single three-token line.");
            Assert.That(decoded, Is.True);
            Assert.That(decodedInput, Is.EqualTo(inputPath));
            Assert.That(decodedOutput, Is.EqualTo(outputPath));
        }

        /// <summary>
        /// What: lines that are not a well-formed run request are rejected instead of being guessed at.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("run")]
        [TestCase("run onlyone")]
        [TestCase("run a b c")]
        [TestCase("walk YQ== Yg==")]
        [TestCase("run !!! Yg==")]
        [TestCase("run  Yg==")]
        public void TryDecodeRequestLine_MalformedLine_ReturnsFalse(string line)
        {
            bool decoded = TransformWorkerServeProtocol.TryDecodeRequestLine(line, out string inputPath, out string outputPath);

            Assert.That(decoded, Is.False);
            Assert.That(inputPath, Is.Null);
            Assert.That(outputPath, Is.Null);
        }

        /// <summary>
        /// What: an empty path is rejected by the encoder rather than producing a request the worker
        /// would decode as an empty file name.
        /// </summary>
        [Test]
        public void EncodeRequestLine_EmptyPath_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => TransformWorkerServeProtocol.EncodeRequestLine(string.Empty, "/out"));
            Assert.Throws<System.ArgumentException>(() => TransformWorkerServeProtocol.EncodeRequestLine("/in", string.Empty));
        }

        /// <summary>
        /// What: a response header round-trips the exit code and the diagnostics byte count.
        /// </summary>
        [Test]
        public void ResponseHeader_RoundTripsExitCodeAndByteCount()
        {
            string header = TransformWorkerServeProtocol.EncodeResponseHeader(-7, 1234);
            bool parsed = TransformWorkerServeProtocol.TryParseResponseHeader(header, out int exitCode, out int byteCount);

            Assert.That(parsed, Is.True);
            Assert.That(exitCode, Is.EqualTo(-7));
            Assert.That(byteCount, Is.EqualTo(1234));
        }

        /// <summary>
        /// What: a header with the wrong prefix, wrong arity, non-numeric fields, or a negative byte
        /// count is rejected so the host treats the conversation as broken.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("noise")]
        [TestCase("__ULOOP_TRANSFORM_RESULT__ 0")]
        [TestCase("__ULOOP_TRANSFORM_RESULT__ zero 0")]
        [TestCase("__ULOOP_TRANSFORM_RESULT__ 0 -1")]
        [TestCase("__ULOOP_TRANSFORM_RESULT__ 0 0 extra")]
        public void TryParseResponseHeader_MalformedHeader_ReturnsFalse(string header)
        {
            bool parsed = TransformWorkerServeProtocol.TryParseResponseHeader(header, out int _, out int _);

            Assert.That(parsed, Is.False);
        }

        /// <summary>
        /// What: diagnostics round-trip through one base64 line, including embedded newlines and a line
        /// that equals the response marker, which is exactly what an end-marker protocol could not carry.
        /// </summary>
        [Test]
        public void Diagnostics_RoundTripMarkerLikeMultilineText()
        {
            string diagnostics = "line one\n" + TransformWorkerServeProtocol.ResultPrefix + " 0 0\n診断 line three";

            string payload = TransformWorkerServeProtocol.EncodeDiagnostics(diagnostics, out int byteCount);
            bool decoded = TransformWorkerServeProtocol.TryDecodeDiagnostics(payload, byteCount, out string roundTripped);

            Assert.That(payload.Contains("\n"), Is.False, "The payload must stay on one line.");
            Assert.That(byteCount, Is.EqualTo(Encoding.UTF8.GetByteCount(diagnostics)));
            Assert.That(decoded, Is.True);
            Assert.That(roundTripped, Is.EqualTo(diagnostics));
        }

        /// <summary>
        /// What: diagnostics beyond the cap are truncated to the cap with a visible suffix, so an
        /// unbounded exception dump cannot stall the response frame.
        /// </summary>
        [Test]
        public void EncodeDiagnostics_OverCap_TruncatesWithSuffix()
        {
            string oversized = new string('x', TransformWorkerServeProtocol.MaxDiagnosticBytes * 2);

            string payload = TransformWorkerServeProtocol.EncodeDiagnostics(oversized, out int byteCount);
            bool decoded = TransformWorkerServeProtocol.TryDecodeDiagnostics(payload, byteCount, out string truncated);

            Assert.That(byteCount, Is.EqualTo(TransformWorkerServeProtocol.MaxDiagnosticBytes));
            Assert.That(decoded, Is.True);
            Assert.That(truncated.EndsWith(TransformWorkerServeProtocol.DiagnosticsTruncatedSuffix), Is.True);
            Assert.That(truncated.StartsWith("xxxx"), Is.True);
        }

        /// <summary>
        /// What: a payload whose decoded length does not match the header's byte count, or that is not
        /// base64 at all, is rejected instead of being accepted as partial diagnostics.
        /// </summary>
        [Test]
        public void TryDecodeDiagnostics_LengthMismatchOrInvalidBase64_ReturnsFalse()
        {
            string payload = TransformWorkerServeProtocol.EncodeDiagnostics("hello", out int byteCount);

            Assert.That(TransformWorkerServeProtocol.TryDecodeDiagnostics(payload, byteCount + 1, out string _), Is.False);
            Assert.That(TransformWorkerServeProtocol.TryDecodeDiagnostics("%%%not base64%%%", byteCount, out string _), Is.False);
            Assert.That(TransformWorkerServeProtocol.TryDecodeDiagnostics(null, byteCount, out string _), Is.False);
        }

        /// <summary>
        /// What: empty diagnostics encode as an empty payload with byte count zero and decode back to empty.
        /// </summary>
        [Test]
        public void Diagnostics_Empty_RoundTripsAsZeroBytes()
        {
            string payload = TransformWorkerServeProtocol.EncodeDiagnostics(null, out int byteCount);
            bool decoded = TransformWorkerServeProtocol.TryDecodeDiagnostics(payload, byteCount, out string roundTripped);

            Assert.That(byteCount, Is.EqualTo(0));
            Assert.That(payload, Is.EqualTo(string.Empty));
            Assert.That(decoded, Is.True);
            Assert.That(roundTripped, Is.EqualTo(string.Empty));
        }
    }
}
