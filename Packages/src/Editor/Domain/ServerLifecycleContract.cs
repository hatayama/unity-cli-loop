using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Result value for server initialization use cases.
    /// </summary>
    /// <typeparam name="TServerInstance">Server instance type owned by the application boundary.</typeparam>
    public sealed class ServerInitializationResult<TServerInstance>
        where TServerInstance : class
    {
        public bool Success { get; }

        public bool IsRunning { get; }

        public string Message { get; }

        public TServerInstance ServerInstance { get; }

        private ServerInitializationResult(
            bool success,
            bool isRunning,
            string message,
            TServerInstance serverInstance)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(message), "message must describe the server initialization result");

            Success = success;
            IsRunning = isRunning;
            Message = message;
            ServerInstance = serverInstance;
        }

        public static ServerInitializationResult<TServerInstance> Running(TServerInstance serverInstance)
        {
            Debug.Assert(serverInstance != null, "serverInstance must be present when initialization succeeds");

            return new ServerInitializationResult<TServerInstance>(
                true,
                true,
                ServerLifecycleMessages.InitializationSucceeded,
                serverInstance);
        }

        public static ServerInitializationResult<TServerInstance> Failed(string message)
        {
            return new ServerInitializationResult<TServerInstance>(
                false,
                false,
                message,
                null);
        }
    }

    /// <summary>
    /// Result value for server shutdown use cases.
    /// </summary>
    public sealed class ServerShutdownResult
    {
        public bool Success { get; }

        public string Message { get; }

        private ServerShutdownResult(bool success, string message)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(message), "message must describe the server shutdown result");

            Success = success;
            Message = message;
        }

        public static ServerShutdownResult Stopped()
        {
            return new ServerShutdownResult(true, ServerLifecycleMessages.ShutdownSucceeded);
        }

        public static ServerShutdownResult AlreadyStopped()
        {
            return new ServerShutdownResult(true, ServerLifecycleMessages.ShutdownAlreadyStopped);
        }

        public static ServerShutdownResult Failed(string message)
        {
            return new ServerShutdownResult(false, message);
        }
    }

    /// <summary>
    /// Lifecycle messages live with the domain result because callers compare behavior, not transport DTOs.
    /// </summary>
    public static class ServerLifecycleMessages
    {
        public const string InitializationSucceeded = "Server initialization completed successfully";
        public const string ShutdownAlreadyStopped = "Server was not running";
        public const string ShutdownSucceeded = "Server shutdown completed successfully";
    }
}
