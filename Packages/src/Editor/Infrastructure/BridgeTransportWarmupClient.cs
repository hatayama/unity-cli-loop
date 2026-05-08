using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Sends a local bridge request through the same project IPC transport used by external CLI clients.
    /// </summary>
    internal static class BridgeTransportWarmupClient
    {
        private const string ContentLengthHeader = "Content-Length:";
        private const int MaxHeaderByteCount = 8192;
        private static readonly byte[] HeaderSeparatorBytes = { 13, 10, 13, 10 };

        internal static Task SendProjectIpcRequestAsync(
            string projectRoot,
            string requestJson,
            CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(projectRoot), "projectRoot must not be empty");
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(requestJson), "requestJson must not be empty");

            BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(projectRoot);
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                using Stream stream = ConnectToEndpoint(endpoint, ct);
                await WriteFrameAsync(stream, requestJson, ct);
                await ReadResponseFrameAsync(stream, ct);
            }, ct);
        }

        private static Stream ConnectToEndpoint(BridgeTransportEndpoint endpoint, CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(endpoint != null, "endpoint must not be null");

            ct.ThrowIfCancellationRequested();
            switch (endpoint.Kind)
            {
                case BridgeTransportKind.UnixDomainSocket:
                    return ConnectToUnixDomainSocket(endpoint);
                case BridgeTransportKind.WindowsNamedPipe:
                    return ConnectToWindowsNamedPipe(endpoint);
                default:
                    throw new ArgumentOutOfRangeException(nameof(endpoint));
            }
        }

        private static Stream ConnectToUnixDomainSocket(BridgeTransportEndpoint endpoint)
        {
            Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            bool connected = false;
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(endpoint.Path));
                connected = true;
                return new NetworkStream(socket, ownsSocket: true);
            }
            finally
            {
                if (!connected)
                {
                    socket.Dispose();
                }
            }
        }

        private static Stream ConnectToWindowsNamedPipe(BridgeTransportEndpoint endpoint)
        {
            NamedPipeClientStream pipe = new NamedPipeClientStream(
                ".",
                endpoint.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            bool connected = false;
            try
            {
                pipe.Connect(5000);
                connected = true;
                return pipe;
            }
            finally
            {
                if (!connected)
                {
                    pipe.Dispose();
                }
            }
        }

        private static async Task WriteFrameAsync(
            Stream stream,
            string requestJson,
            CancellationToken ct)
        {
            byte[] payload = Encoding.UTF8.GetBytes(requestJson);
            string header = $"{ContentLengthHeader} {payload.Length}\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct);
            await stream.WriteAsync(payload, 0, payload.Length, ct);
        }

        private static async Task ReadResponseFrameAsync(Stream stream, CancellationToken ct)
        {
            int contentLength = await ReadContentLengthAsync(stream, ct);
            byte[] payload = new byte[contentLength];
            await ReadPayloadAsync(stream, payload, ct);
        }

        private static async Task<int> ReadContentLengthAsync(Stream stream, CancellationToken ct)
        {
            List<byte> headerBytes = new List<byte>();
            byte[] buffer = new byte[1];
            while (!EndsWithHeaderSeparator(headerBytes))
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("Project IPC warmup ended before response headers were complete.");
                }

                headerBytes.Add(buffer[0]);
                if (headerBytes.Count > MaxHeaderByteCount)
                {
                    throw new InvalidOperationException("Project IPC warmup response headers exceeded the maximum size.");
                }
            }

            return ParseContentLength(headerBytes);
        }

        private static bool EndsWithHeaderSeparator(List<byte> headerBytes)
        {
            if (headerBytes.Count < HeaderSeparatorBytes.Length)
            {
                return false;
            }

            int startIndex = headerBytes.Count - HeaderSeparatorBytes.Length;
            for (int i = 0; i < HeaderSeparatorBytes.Length; i++)
            {
                if (headerBytes[startIndex + i] != HeaderSeparatorBytes[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ParseContentLength(List<byte> headerBytes)
        {
            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                if (!line.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = line.Substring(ContentLengthHeader.Length).Trim();
                if (int.TryParse(value, out int contentLength) && contentLength >= 0)
                {
                    return contentLength;
                }

                throw new InvalidOperationException($"Project IPC warmup response had an invalid Content-Length: {line}");
            }

            throw new InvalidOperationException("Project IPC warmup response did not include Content-Length.");
        }

        private static async Task ReadPayloadAsync(
            Stream stream,
            byte[] payload,
            CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < payload.Length)
            {
                int bytesRead = await stream.ReadAsync(payload, totalRead, payload.Length - totalRead, ct);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("Project IPC warmup ended before response payload was complete.");
                }

                totalRead += bytesRead;
            }
        }
    }
}
