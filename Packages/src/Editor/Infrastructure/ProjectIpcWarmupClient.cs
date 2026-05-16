using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Sends a local bridge request through the same project IPC transport used by external CLI clients.
    /// </summary>
    internal sealed class ProjectIpcWarmupClient
    {
        private const string ContentLengthHeader = "Content-Length:";
        private const int EndpointConnectTimeoutMilliseconds = 5000;
        private const int MaxHeaderByteCount = 8192;
        private const int MaxPayloadByteCount = BufferConfig.MAX_MESSAGE_SIZE;
        private readonly byte[] _headerSeparatorBytes = { 13, 10, 13, 10 };

        internal Task SendProjectIpcRequestAsync(
            string projectRoot,
            string requestJson,
            CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(projectRoot), "projectRoot must not be empty");
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(requestJson), "requestJson must not be empty");

            BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(projectRoot);
            // Why: server lifecycle callbacks run on Unity's editor thread; connecting on a worker
            // thread lets the local readiness request exercise the same IPC path as an external CLI
            // command without blocking editor startup or recovery callbacks.
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                using Stream stream = await ConnectToEndpointAsync(endpoint, ct);
                await WriteFrameAsync(stream, requestJson, ct);
                string responseJson = await ReadResponseFrameAsync(stream, ct);
                ValidateJsonRpcSuccessResponse(responseJson);
            }, ct);
        }

        private async Task<Stream> ConnectToEndpointAsync(BridgeTransportEndpoint endpoint, CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(endpoint != null, "endpoint must not be null");

            ct.ThrowIfCancellationRequested();
            switch (endpoint.Kind)
            {
                case BridgeTransportKind.UnixDomainSocket:
                    using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        connectCts.CancelAfter(EndpointConnectTimeoutMilliseconds);
                        return await ConnectToUnixDomainSocketAsync(endpoint, connectCts.Token);
                    }
                case BridgeTransportKind.WindowsNamedPipe:
                    return ConnectToWindowsNamedPipe(endpoint);
                default:
                    throw new ArgumentOutOfRangeException(nameof(endpoint));
            }
        }

        private async Task<Stream> ConnectToUnixDomainSocketAsync(BridgeTransportEndpoint endpoint, CancellationToken ct)
        {
            Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            bool connected = false;
            try
            {
                Task connectTask = Task.Factory.FromAsync<EndPoint>(
                    socket.BeginConnect,
                    socket.EndConnect,
                    new UnixDomainSocketEndPoint(endpoint.Path),
                    null);
                await WaitForConnectAsync(connectTask, socket, ct);
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

        private Stream ConnectToWindowsNamedPipe(BridgeTransportEndpoint endpoint)
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

        private async Task WriteFrameAsync(
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

        private async Task<string> ReadResponseFrameAsync(Stream stream, CancellationToken ct)
        {
            int contentLength = await ReadContentLengthAsync(stream, ct);
            byte[] payload = new byte[contentLength];
            await ReadPayloadAsync(stream, payload, ct);
            return Encoding.UTF8.GetString(payload);
        }

        private async Task<int> ReadContentLengthAsync(Stream stream, CancellationToken ct)
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

        private bool EndsWithHeaderSeparator(List<byte> headerBytes)
        {
            if (headerBytes.Count < _headerSeparatorBytes.Length)
            {
                return false;
            }

            int startIndex = headerBytes.Count - _headerSeparatorBytes.Length;
            for (int i = 0; i < _headerSeparatorBytes.Length; i++)
            {
                if (headerBytes[startIndex + i] != _headerSeparatorBytes[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal int ParseContentLength(List<byte> headerBytes)
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
                if (int.TryParse(value, out int contentLength) && contentLength >= 0 && contentLength <= MaxPayloadByteCount)
                {
                    return contentLength;
                }

                throw new InvalidOperationException($"Project IPC warmup response had an invalid Content-Length: {line}");
            }

            throw new InvalidOperationException("Project IPC warmup response did not include Content-Length.");
        }

        internal void ValidateJsonRpcSuccessResponse(string responseJson)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(responseJson), "responseJson must not be empty");

            JObject response = JObject.Parse(responseJson);
            JToken errorToken = response["error"];
            if (errorToken != null && errorToken.Type != JTokenType.Null)
            {
                string errorMessage = errorToken["message"]?.ToString();
                if (string.IsNullOrWhiteSpace(errorMessage))
                {
                    errorMessage = errorToken.ToString();
                }

                throw new InvalidOperationException($"Project IPC warmup returned JSON-RPC error: {errorMessage}");
            }

            JToken resultToken = response["result"];
            if (resultToken == null || resultToken.Type == JTokenType.Null)
            {
                throw new InvalidOperationException("Project IPC warmup response did not include a JSON-RPC result.");
            }
        }

        private async Task WaitForConnectAsync(Task connectTask, Socket socket, CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(connectTask != null, "connectTask must not be null");
            System.Diagnostics.Debug.Assert(socket != null, "socket must not be null");

            Task cancellationTask = Task.Delay(Timeout.Infinite, ct);
            Task completedTask = await Task.WhenAny(connectTask, cancellationTask);
            if (completedTask == connectTask)
            {
                await connectTask;
                return;
            }

            // Why: Unity 2022 does not expose a cancellable Unix socket connect API,
            // so disposing the socket is the only reliable way to release the pending OS connect.
            socket.Dispose();
            ObserveConnectFault(connectTask);
            ct.ThrowIfCancellationRequested();
        }

        private void ObserveConnectFault(Task connectTask)
        {
            _ = connectTask.ContinueWith(
                completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private async Task ReadPayloadAsync(
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
