using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// This file is compiled twice: into the Unity editor assembly (host side) and into the
// out-of-process transform worker (see TransformWorkerBootstrap.CollectWorkerSourcePaths).
// It must therefore stay free of Unity and Newtonsoft references.
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Line protocol between the Editor and a resident transform worker (`worker.dll --serve`).
    /// Request: one line, `run &lt;base64 input path&gt; &lt;base64 output path&gt;`. Response: two lines,
    /// a header `__ULOOP_TRANSFORM_RESULT__ &lt;exitCode&gt; &lt;diagnosticByteCount&gt;` followed by the
    /// diagnostics as one base64 line. Why length-prefixed instead of an end marker: worker
    /// diagnostics can contain any text, including a line equal to a marker, and a fixed
    /// two-line frame cannot be desynchronized by its own payload.
    /// </summary>
    internal static class TransformWorkerServeProtocol
    {
        public const string ServeArgument = "--serve";
        public const string RunCommand = "run";
        public const string QuitCommand = "__ULOOP_TRANSFORM_QUIT__";
        public const string ResultPrefix = "__ULOOP_TRANSFORM_RESULT__";
        public const int MalformedRequestExitCode = 2;

        // Why bounded: diagnostics travel as a single line through a pipe the host reads with a
        // deadline; an unbounded exception dump or a chatty dependency must not stall the frame.
        public const int MaxDiagnosticBytes = 64 * 1024;
        public const string DiagnosticsTruncatedSuffix = "\n[diagnostics truncated]";

        public const int DefaultIdleTimeoutMilliseconds = 5 * 60 * 1000;

        public static string EncodeRequestLine(string inputJsonPath, string outputJsonPath)
        {
            if (string.IsNullOrEmpty(inputJsonPath))
            {
                throw new ArgumentException("inputJsonPath must not be empty.", nameof(inputJsonPath));
            }

            if (string.IsNullOrEmpty(outputJsonPath))
            {
                throw new ArgumentException("outputJsonPath must not be empty.", nameof(outputJsonPath));
            }

            return RunCommand + " " + ToBase64(inputJsonPath) + " " + ToBase64(outputJsonPath);
        }

        public static bool TryDecodeRequestLine(string line, out string inputJsonPath, out string outputJsonPath)
        {
            inputJsonPath = null;
            outputJsonPath = null;
            if (line == null)
            {
                return false;
            }

            string[] parts = line.Split(' ');
            if (parts.Length != 3 || parts[0] != RunCommand)
            {
                return false;
            }

            return TryFromBase64(parts[1], out inputJsonPath) && TryFromBase64(parts[2], out outputJsonPath);
        }

        public static string EncodeResponseHeader(int exitCode, int diagnosticByteCount)
        {
            if (diagnosticByteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(diagnosticByteCount));
            }

            return ResultPrefix + " "
                + exitCode.ToString(CultureInfo.InvariantCulture) + " "
                + diagnosticByteCount.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseResponseHeader(string line, out int exitCode, out int diagnosticByteCount)
        {
            exitCode = 0;
            diagnosticByteCount = 0;
            if (line == null)
            {
                return false;
            }

            string[] parts = line.Split(' ');
            if (parts.Length != 3 || parts[0] != ResultPrefix)
            {
                return false;
            }

            return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out exitCode)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out diagnosticByteCount)
                && diagnosticByteCount >= 0;
        }

        /// <summary>
        /// Encodes diagnostics as one base64 line, truncating to <see cref="MaxDiagnosticBytes"/>.
        /// </summary>
        public static string EncodeDiagnostics(string diagnostics, out int byteCount)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(diagnostics ?? string.Empty);
            if (bytes.Length > MaxDiagnosticBytes)
            {
                byte[] suffix = Encoding.UTF8.GetBytes(DiagnosticsTruncatedSuffix);
                byte[] truncated = new byte[MaxDiagnosticBytes];
                Buffer.BlockCopy(bytes, 0, truncated, 0, MaxDiagnosticBytes - suffix.Length);
                Buffer.BlockCopy(suffix, 0, truncated, MaxDiagnosticBytes - suffix.Length, suffix.Length);
                bytes = truncated;
            }

            byteCount = bytes.Length;
            return Convert.ToBase64String(bytes);
        }

        public static bool TryDecodeDiagnostics(string line, int expectedByteCount, out string diagnostics)
        {
            diagnostics = null;
            if (line == null)
            {
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(line);
            }
            catch (FormatException)
            {
                return false;
            }

            if (bytes.Length != expectedByteCount)
            {
                return false;
            }

            // Why not decode strictly: a truncation cut may split a multi-byte sequence; the
            // replacement character is preferable to failing the whole frame over one glyph.
            diagnostics = Encoding.UTF8.GetString(bytes);
            return true;
        }

        private static string ToBase64(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static bool TryFromBase64(string encoded, out string value)
        {
            value = null;
            try
            {
                value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                return false;
            }

            if (value.Length == 0)
            {
                value = null;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The worker-side request loop of the resident mode. Reads request lines until quit, end of
    /// input, or an idle timeout while waiting for the next request, runs one transform per
    /// request with the process stdout captured into the response, and writes one response frame
    /// per request. Kept free of process concerns so tests can drive it over in-memory pipes.
    /// </summary>
    internal static class TransformWorkerServeLoop
    {
        public delegate int TransformHandler(string inputJsonPath, string outputJsonPath);

        public const int ExitCodeNormal = 0;

        /// <summary>
        /// Runs until quit, end of input, or idle. Returns the process exit code.
        /// </summary>
        /// <param name="input">Request lines (the process stdin).</param>
        /// <param name="protocolOutput">Response frames. Must be the original stdout writer, captured
        /// before this call, because the loop redirects Console.Out while a transform runs.</param>
        /// <param name="transform">Executes one request and returns its exit code.</param>
        /// <param name="idleTimeoutMilliseconds">Exit when no request arrives within this time while
        /// waiting. Never fires while a transform is running.</param>
        public static int Run(
            TextReader input,
            TextWriter protocolOutput,
            TransformHandler transform,
            int idleTimeoutMilliseconds)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (protocolOutput == null)
            {
                throw new ArgumentNullException(nameof(protocolOutput));
            }

            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            if (idleTimeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(idleTimeoutMilliseconds));
            }

            while (true)
            {
                string line = ReadLineOrIdle(input, idleTimeoutMilliseconds);
                if (line == null || line == TransformWorkerServeProtocol.QuitCommand)
                {
                    return ExitCodeNormal;
                }

                if (!TransformWorkerServeProtocol.TryDecodeRequestLine(line, out string inputJsonPath, out string outputJsonPath))
                {
                    WriteFrame(protocolOutput, TransformWorkerServeProtocol.MalformedRequestExitCode, "malformed request line: " + line);
                    continue;
                }

                (int exitCode, string diagnostics) = ExecuteCapturingConsoleOut(transform, inputJsonPath, outputJsonPath);
                WriteFrame(protocolOutput, exitCode, diagnostics);
            }
        }

        // Why race a delay against the read instead of a watchdog thread: the idle exit must only
        // fire between requests. A watchdog could kill the process right after a request was
        // accepted but before the transform started.
        private static string ReadLineOrIdle(TextReader input, int idleTimeoutMilliseconds)
        {
            Task<string> readTask = Task.Run(() => input.ReadLine());
            Task delayTask = Task.Delay(idleTimeoutMilliseconds);
            Task completed = Task.WhenAny(readTask, delayTask).GetAwaiter().GetResult();
            if (!ReferenceEquals(completed, readTask))
            {
                return null;
            }

            try
            {
                return readTask.GetAwaiter().GetResult();
            }
            catch (IOException)
            {
                // The host closed the pipe; treat it like end of input.
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        // Why catch everything here: this is the process boundary of one request. The failure is
        // reported to the host as exit code 1 with the full exception text, and the process stays
        // alive for the next request, exactly like a one-shot worker exiting non-zero would.
        private static (int exitCode, string diagnostics) ExecuteCapturingConsoleOut(
            TransformHandler transform,
            string inputJsonPath,
            string outputJsonPath)
        {
            TextWriter originalOut = Console.Out;
            StringWriter captured = new StringWriter();
            Console.SetOut(captured);
            int exitCode;
            try
            {
                exitCode = transform(inputJsonPath, outputJsonPath);
            }
            catch (Exception ex)
            {
                exitCode = 1;
                captured.WriteLine(ex.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            return (exitCode, captured.ToString());
        }

        private static void WriteFrame(TextWriter protocolOutput, int exitCode, string diagnostics)
        {
            string payload = TransformWorkerServeProtocol.EncodeDiagnostics(diagnostics, out int byteCount);
            protocolOutput.WriteLine(TransformWorkerServeProtocol.EncodeResponseHeader(exitCode, byteCount));
            protocolOutput.WriteLine(payload);
            protocolOutput.Flush();
        }
    }
}
