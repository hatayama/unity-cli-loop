namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class UnixEndpointSecurityConstants
    {
        public const string ParentPath = "/tmp";
        public const string EndpointDirectoryPrefix = "uloop-";
        public const uint RootUserId = 0;
        public const uint StickyBit = 0x200;
        public const uint EndpointDirectoryMode = 0x1C0;
        public const uint SocketMode = 0x180;
    }

    internal enum UnixFileKind
    {
        Missing,
        Directory,
        SymbolicLink,
        Socket,
        RegularFile,
        Other
    }

    internal readonly struct UnixFileMetadata
    {
        public bool ReadSuccess { get; }
        public int ErrorCode { get; }
        public UnixFileKind Kind { get; }
        public uint OwnerUserId { get; }
        public uint Mode { get; }

        private UnixFileMetadata(
            bool readSuccess,
            int errorCode,
            UnixFileKind kind,
            uint ownerUserId,
            uint mode)
        {
            ReadSuccess = readSuccess;
            ErrorCode = errorCode;
            Kind = kind;
            OwnerUserId = ownerUserId;
            Mode = mode;
        }

        public static UnixFileMetadata Existing(UnixFileKind kind, uint ownerUserId, uint mode)
        {
            return new UnixFileMetadata(true, 0, kind, ownerUserId, mode);
        }

        public static UnixFileMetadata Missing()
        {
            return new UnixFileMetadata(true, 0, UnixFileKind.Missing, 0, 0);
        }

        public static UnixFileMetadata Failure(int errorCode)
        {
            return new UnixFileMetadata(false, errorCode, UnixFileKind.Other, 0, 0);
        }
    }

    internal readonly struct UnixNativeOperationResult
    {
        public bool Success { get; }
        public int ErrorCode { get; }

        private UnixNativeOperationResult(bool success, int errorCode)
        {
            Success = success;
            ErrorCode = errorCode;
        }

        public static UnixNativeOperationResult Successful()
        {
            return new UnixNativeOperationResult(true, 0);
        }

        public static UnixNativeOperationResult Failure(int errorCode)
        {
            return new UnixNativeOperationResult(false, errorCode);
        }
    }

    internal static class UnixNativeError
    {
        public const int NoEntry = 2;
        public const int AlreadyExists = 17;
    }

    internal interface IUnixNativeFileSystem
    {
        uint GetEffectiveUserId();
        UnixFileMetadata ReadMetadata(string path, bool followSymbolicLinks);
        UnixNativeOperationResult CreateDirectory(string path, uint mode);
        UnixNativeOperationResult ChangeMode(string path, uint mode);
    }

    internal readonly struct UnixEndpointSecurityResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }

        private UnixEndpointSecurityResult(bool success, string errorMessage)
        {
            Success = success;
            ErrorMessage = errorMessage;
        }

        public static UnixEndpointSecurityResult Successful()
        {
            return new UnixEndpointSecurityResult(true, string.Empty);
        }

        public static UnixEndpointSecurityResult Failure(string errorMessage)
        {
            return new UnixEndpointSecurityResult(false, errorMessage);
        }
    }

    /// <summary>
    /// Validates the Unix parent, per-user endpoint directory, and socket before filesystem mutation.
    /// </summary>
    internal sealed class UnixEndpointSecurityPolicy
    {
        private readonly IUnixNativeFileSystem _fileSystem;

        public UnixEndpointSecurityPolicy(IUnixNativeFileSystem fileSystem)
        {
            System.Diagnostics.Debug.Assert(fileSystem != null, "fileSystem must not be null");
            _fileSystem = fileSystem;
        }

        public UnixEndpointSecurityResult EnsureEndpointDirectory(string endpointDirectoryPath)
        {
            System.Diagnostics.Debug.Assert(
                !string.IsNullOrWhiteSpace(endpointDirectoryPath),
                "endpointDirectoryPath must not be empty");

            UnixEndpointSecurityResult parentResult = ValidateParent();
            if (!parentResult.Success)
            {
                return parentResult;
            }

            UnixFileMetadata endpointMetadata = _fileSystem.ReadMetadata(
                endpointDirectoryPath,
                followSymbolicLinks: false);
            if (!endpointMetadata.ReadSuccess)
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Failed to inspect Unix endpoint directory {endpointDirectoryPath}: errno {endpointMetadata.ErrorCode}");
            }

            if (endpointMetadata.Kind == UnixFileKind.Missing)
            {
                UnixNativeOperationResult createResult = _fileSystem.CreateDirectory(
                    endpointDirectoryPath,
                    UnixEndpointSecurityConstants.EndpointDirectoryMode);
                if (!createResult.Success && createResult.ErrorCode != UnixNativeError.AlreadyExists)
                {
                    return UnixEndpointSecurityResult.Failure(
                        $"Failed to create Unix endpoint directory {endpointDirectoryPath}: errno {createResult.ErrorCode}");
                }

                // Why: EEXIST can mean another process won the creation race. Both successful
                // creation and EEXIST must converge on the same no-follow owner/mode validation.
                endpointMetadata = _fileSystem.ReadMetadata(
                    endpointDirectoryPath,
                    followSymbolicLinks: false);
                if (!endpointMetadata.ReadSuccess)
                {
                    return UnixEndpointSecurityResult.Failure(
                        $"Failed to reinspect Unix endpoint directory {endpointDirectoryPath}: errno {endpointMetadata.ErrorCode}");
                }
            }

            return ValidateEndpointDirectoryMetadata(endpointDirectoryPath, endpointMetadata);
        }

        public UnixEndpointSecurityResult ValidateStaleSocket(string socketPath)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(socketPath), "socketPath must not be empty");

            UnixFileMetadata metadata = _fileSystem.ReadMetadata(socketPath, followSymbolicLinks: false);
            if (!metadata.ReadSuccess)
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Failed to inspect Unix socket {socketPath}: errno {metadata.ErrorCode}");
            }
            if (metadata.Kind == UnixFileKind.Missing)
            {
                return UnixEndpointSecurityResult.Successful();
            }
            if (metadata.Kind != UnixFileKind.Socket ||
                metadata.OwnerUserId != _fileSystem.GetEffectiveUserId())
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Refusing to remove untrusted existing Unix endpoint {socketPath}");
            }

            return UnixEndpointSecurityResult.Successful();
        }

        public UnixEndpointSecurityResult RestrictSocket(string socketPath)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(socketPath), "socketPath must not be empty");

            // Why: bind may honor a permissive process umask, but the containing directory is
            // already 0700, so other users cannot reach the socket before this chmod to 0600.
            UnixNativeOperationResult result = _fileSystem.ChangeMode(
                socketPath,
                UnixEndpointSecurityConstants.SocketMode);
            if (!result.Success)
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Failed to restrict Unix socket {socketPath} to mode 0600: errno {result.ErrorCode}");
            }

            UnixFileMetadata metadata = _fileSystem.ReadMetadata(socketPath, followSymbolicLinks: false);
            if (!metadata.ReadSuccess ||
                metadata.Kind != UnixFileKind.Socket ||
                metadata.OwnerUserId != _fileSystem.GetEffectiveUserId() ||
                metadata.Mode != UnixEndpointSecurityConstants.SocketMode)
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Unix socket {socketPath} did not retain owner-only mode 0600");
            }

            return UnixEndpointSecurityResult.Successful();
        }

        private UnixEndpointSecurityResult ValidateParent()
        {
            UnixFileMetadata parentNoFollow = _fileSystem.ReadMetadata(
                UnixEndpointSecurityConstants.ParentPath,
                followSymbolicLinks: false);
            if (!parentNoFollow.ReadSuccess ||
                (parentNoFollow.Kind != UnixFileKind.Directory &&
                 parentNoFollow.Kind != UnixFileKind.SymbolicLink))
            {
                return UnixEndpointSecurityResult.Failure(
                    "Unix endpoint parent /tmp must be a directory or a symbolic link to one");
            }

            UnixFileMetadata parentFollowed = _fileSystem.ReadMetadata(
                UnixEndpointSecurityConstants.ParentPath,
                followSymbolicLinks: true);
            if (!parentFollowed.ReadSuccess ||
                parentFollowed.Kind != UnixFileKind.Directory ||
                parentFollowed.OwnerUserId != UnixEndpointSecurityConstants.RootUserId ||
                (parentFollowed.Mode & UnixEndpointSecurityConstants.StickyBit) == 0)
            {
                return UnixEndpointSecurityResult.Failure(
                    "Resolved Unix endpoint parent /tmp must be a root-owned sticky directory");
            }

            return UnixEndpointSecurityResult.Successful();
        }

        private UnixEndpointSecurityResult ValidateEndpointDirectoryMetadata(
            string endpointDirectoryPath,
            UnixFileMetadata metadata)
        {
            if (metadata.Kind != UnixFileKind.Directory)
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Unix endpoint directory {endpointDirectoryPath} must be a real directory");
            }
            if (metadata.OwnerUserId != _fileSystem.GetEffectiveUserId())
            {
                return UnixEndpointSecurityResult.Failure(
                    $"Unix endpoint directory {endpointDirectoryPath} is not owned by the current user");
            }
            if (metadata.Mode != UnixEndpointSecurityConstants.EndpointDirectoryMode)
            {
                // Why not chmod an existing directory: an attacker-controlled or accidentally
                // shared path must be rejected, not transformed into a path the process trusts.
                return UnixEndpointSecurityResult.Failure(
                    $"Unix endpoint directory {endpointDirectoryPath} must already have mode 0700");
            }

            return UnixEndpointSecurityResult.Successful();
        }
    }
}
