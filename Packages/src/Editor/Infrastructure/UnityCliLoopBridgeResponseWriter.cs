using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Frames and serializes JSON responses written to project IPC client streams.
    /// </summary>
    internal static class UnityCliLoopBridgeResponseWriter
    {
        /// <summary>
        /// Creates a Content-Length framed message for JSON-RPC 2.0 communication.
        /// </summary>
        /// <param name="jsonContent">The JSON content to frame</param>
        /// <returns>The framed message with Content-Length header</returns>
        internal static string CreateContentLengthFrame(string jsonContent)
        {
            if (string.IsNullOrEmpty(jsonContent))
            {
                return string.Empty;
            }

            // Calculate content length in bytes (UTF-8 encoding)
            int contentLength = Encoding.UTF8.GetByteCount(jsonContent);

            // Create the framed message: Content-Length: <n>\r\n\r\n<json_content>
            return $"Content-Length: {contentLength}\r\n\r\n{jsonContent}";
        }

        private static async Task WriteJsonResponseAsync(
            Stream stream,
            string responseJson,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(responseJson))
            {
                return;
            }

            if (!stream.CanWrite || ct.IsCancellationRequested)
            {
                return;
            }

            string framedResponse = CreateContentLengthFrame(responseJson);
            byte[] responseData = Encoding.UTF8.GetBytes(framedResponse);
            await stream.WriteAsync(responseData, 0, responseData.Length, ct);
        }

        internal static async Task WriteJsonResponseLockedAsync(
            Stream stream,
            SemaphoreSlim streamWriteLock,
            string responseJson,
            CancellationToken ct)
        {
            // Why: heartbeat frames are written from a background timer while the final
            // response is written by the request task; interleaved writes would corrupt
            // Content-Length framing, so all frame writes share one lock per connection.
            await streamWriteLock.WaitAsync(ct);
            try
            {
                await WriteJsonResponseAsync(stream, responseJson, ct);
            }
            finally
            {
                streamWriteLock.Release();
            }
        }
    }
}
