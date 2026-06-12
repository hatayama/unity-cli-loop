using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides Bridge Client Connection behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class BridgeClientConnection : IDisposable
    {
        private readonly Func<bool> _isConnected;

        public string Endpoint { get; }
        public Stream Stream { get; }

        public BridgeClientConnection(string endpoint, Stream stream, Func<bool> isConnected)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(endpoint), "endpoint must not be null or whitespace");
            System.Diagnostics.Debug.Assert(stream != null, "stream must not be null");
            System.Diagnostics.Debug.Assert(isConnected != null, "isConnected must not be null");

            Endpoint = endpoint;
            Stream = stream;
            _isConnected = isConnected;
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    return _isConnected();
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public void Dispose()
        {
            Stream.Dispose();
        }
    }

    /// <summary>
    /// Defines listener operations for bridge transport sessions.
    /// </summary>
    internal interface IBridgeTransportListener : IDisposable
    {
        BridgeTransportEndpoint Endpoint { get; }
        void Start();
        BridgeClientConnection AcceptClient(CancellationToken ct);
        void Stop();
    }

    /// <summary>
    /// Creates Bridge Transport Listener instances with the dependencies required by this module.
    /// </summary>
    internal static class BridgeTransportListenerFactory
    {
        public static IBridgeTransportListener Create(BridgeTransportEndpoint endpoint)
        {
            switch (endpoint.Kind)
            {
                case BridgeTransportKind.UnixDomainSocket:
                    return new UnixDomainSocketBridgeTransportListener(endpoint);
                case BridgeTransportKind.WindowsNamedPipe:
                    return new WindowsNamedPipeBridgeTransportListener(endpoint);
                default:
                    throw new ArgumentOutOfRangeException(nameof(endpoint));
            }
        }
    }

    /// <summary>
    /// Provides Unix Domain Socket Bridge Transport Listener behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class UnixDomainSocketBridgeTransportListener : IBridgeTransportListener
    {
        private Socket _listener;
        private long _nextClientId;

        public BridgeTransportEndpoint Endpoint { get; }

        public UnixDomainSocketBridgeTransportListener(BridgeTransportEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public void Start()
        {
            string directory = Path.GetDirectoryName(Endpoint.Path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(Endpoint.Path))
            {
                File.Delete(Endpoint.Path);
            }

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(Endpoint.Path));
            _listener.Listen(100);
        }

        public BridgeClientConnection AcceptClient(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using CancellationTokenRegistration cancellationRegistration = ct.Register(Stop);
            Socket listener = _listener;
            if (listener == null)
            {
                // Why: a stopped listener must surface as disposal so ServerLoopAsync exits the
                // accept loop and triggers recovery, instead of spinning on NullReferenceException.
                throw new ObjectDisposedException(nameof(UnixDomainSocketBridgeTransportListener));
            }

            Socket client;
            try
            {
                client = listener.Accept();
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            string clientEndpoint = $"{Endpoint.Path}#{Interlocked.Increment(ref _nextClientId)}";
            return new BridgeClientConnection(
                clientEndpoint,
                new NetworkStream(client, ownsSocket: true),
                () => IsSocketConnected(client));
        }

        public void Stop()
        {
            // Why: Stop races between the accept-loop cancellation registration, StopServer on
            // the main thread, and unexpected-exit cleanup on the thread pool. Interlocked hands
            // the socket to exactly one caller so Close and the socket-file delete run once.
            Socket listener = Interlocked.Exchange(ref _listener, null);
            if (listener == null)
            {
                return;
            }

            listener.Close();
            if (File.Exists(Endpoint.Path))
            {
                File.Delete(Endpoint.Path);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private static bool IsSocketConnected(Socket socket)
        {
            return socket.Connected && !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
    }

    /// <summary>
    /// Provides Windows Named Pipe Bridge Transport Listener behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class WindowsNamedPipeBridgeTransportListener : IBridgeTransportListener
    {
        // Why: closing a non-overlapped pipe handle does not cancel a synchronous
        // ConnectNamedPipe wait, so Stop must wake a pending accept with a loopback
        // connection; the short timeout covers the case where no accept is pending.
        private const int WAKE_PENDING_ACCEPT_CONNECT_TIMEOUT_MS = 250;

        private NamedPipeServerStream _activePipe;
        private long _nextClientId;
        // Why: without a stopped state, an accept racing with Stop could create a fresh
        // pipe that nobody can wake, leaving the accept loop blocked forever.
        private volatile bool _stopped;
        private string _ownerOnlySddl;

        public BridgeTransportEndpoint Endpoint { get; }

        public WindowsNamedPipeBridgeTransportListener(BridgeTransportEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public void Start()
        {
            // Why: unit tests construct this listener on macOS/Linux where the Windows
            // token APIs do not exist; production only accepts on the Windows editor.
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
            {
                return;
            }

            // Resolving the SID here makes a broken security setup fail the bind itself
            // instead of throwing inside the accept loop on every iteration.
            _ownerOnlySddl = WindowsOwnerOnlyNamedPipeFactory.BuildCurrentUserOnlySddl();
        }

        public BridgeClientConnection AcceptClient(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfStopped();
            System.Diagnostics.Debug.Assert(_ownerOnlySddl != null, "Start must resolve the owner-only SDDL before accepting");
            NamedPipeServerStream pipe = WindowsOwnerOnlyNamedPipeFactory.CreateServer(Endpoint.PipeName, _ownerOnlySddl);
            _activePipe = pipe;
            // Why: Stop may have run between the stopped check and the assignment above;
            // re-checking after publishing the pipe guarantees one side disposes it.
            if (_stopped)
            {
                Interlocked.Exchange(ref _activePipe, null)?.Dispose();
                ThrowIfStopped();
            }

            using CancellationTokenRegistration cancellationRegistration = ct.Register(Stop);
            bool connected = false;
            try
            {
                try
                {
                    pipe.WaitForConnection();
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (IOException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                // Why: Stop wakes a pending accept with a loopback connection, so a wait that
                // returned after cancellation or stop holds a wake client, not a real CLI session.
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                ThrowIfStopped();

                connected = true;
                string clientEndpoint = $"{Endpoint.Path}#{Interlocked.Increment(ref _nextClientId)}";
                return new BridgeClientConnection(clientEndpoint, pipe, () => pipe.IsConnected);
            }
            finally
            {
                Interlocked.CompareExchange(ref _activePipe, null, pipe);

                if (!connected)
                {
                    pipe.Dispose();
                }
            }
        }

        public void Stop()
        {
            // Why: Stop races between the accept-loop cancellation registration, StopServer
            // on the main thread, and unexpected-exit cleanup on the thread pool; Interlocked
            // hands the pipe to exactly one caller so Dispose runs once.
            _stopped = true;
            NamedPipeServerStream pipe = Interlocked.Exchange(ref _activePipe, null);
            if (pipe == null)
            {
                return;
            }

            // Wake before Dispose: a disposed instance can no longer be connected to,
            // which would leave a blocked synchronous WaitForConnection stuck forever.
            WakePendingAccept();
            pipe.Dispose();
        }

        // Why: closing the pipe handle does not cancel a pending synchronous ConnectNamedPipe
        // wait, so the only reliable release is a loopback connection. The accept loop discards
        // the wake connection through its stopped/cancellation checks. A connect failure means
        // no accept was pending, which leaves nothing to wake or clean up.
        private void WakePendingAccept()
        {
            try
            {
                using NamedPipeClientStream wakeClient = new(
                    ".",
                    Endpoint.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.None);
                wakeClient.Connect(WAKE_PENDING_ACCEPT_CONNECT_TIMEOUT_MS);
            }
            catch (Exception ex)
            {
                // A connect failure when no accept was pending is the expected, harmless case,
                // so this stays at debug level. But it is also the only signal that the single
                // unblock path failed, so record it: if a stuck accept ever causes a shutdown
                // hang, this is the breadcrumb that points here.
                VibeLogger.LogDebug(
                    "wake_pending_accept_failed",
                    ex.Message,
                    new { exceptionType = ex.GetType().Name });
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void ThrowIfStopped()
        {
            if (_stopped)
            {
                // Surface as disposal so ServerLoopAsync exits the accept loop and triggers
                // recovery instead of re-listening on a stopped listener.
                throw new ObjectDisposedException(nameof(WindowsNamedPipeBridgeTransportListener));
            }
        }

    }
}
