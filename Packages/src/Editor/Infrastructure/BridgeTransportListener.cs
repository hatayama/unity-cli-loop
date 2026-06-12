using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

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
        private NamedPipeServerStream _activePipe;
        private long _nextClientId;
        // Why: without a stopped state, an accept racing with Stop could create a fresh
        // pipe that nobody can wake, leaving the accept loop blocked forever.
        private volatile bool _stopped;

        public BridgeTransportEndpoint Endpoint { get; }

        public WindowsNamedPipeBridgeTransportListener(BridgeTransportEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public void Start()
        {
        }

        public BridgeClientConnection AcceptClient(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfStopped();
            NamedPipeServerStream pipe = new(
                Endpoint.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                CreateCurrentUserPipeSecurity());
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
            pipe?.Dispose();
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

        // Why: a named pipe created without an explicit ACL inherits a default security descriptor
        // that lets any local user open the pipe. Because any connected client can invoke
        // execute-dynamic-code (arbitrary C# inside this Editor process), the pipe must be reachable
        // only by the user who owns this Editor. Granting FullControl to the current user's SID alone
        // denies every other local principal, including other interactive/RDP users on a shared host.
        private static PipeSecurity CreateCurrentUserPipeSecurity()
        {
            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            System.Diagnostics.Debug.Assert(currentUser != null, "current Windows user SID must be resolvable");

            PipeSecurity pipeSecurity = new();
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            return pipeSecurity;
        }
    }
}
