using System.Runtime.InteropServices;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides the minimal POSIX metadata operations needed to secure the Unix IPC endpoint.
    /// </summary>
    internal sealed class UnixNativeFileSystem : IUnixNativeFileSystem
    {
        private const int UnsupportedPlatformError = 38;
        private const int UnixNativeStatBufferSize = 144;
        private const uint FileTypeMask = 0xF000;
        private const uint DirectoryType = 0x4000;
        private const uint RegularFileType = 0x8000;
        private const uint SymbolicLinkType = 0xA000;
        private const uint SocketType = 0xC000;
        private const uint PermissionMask = 0x0FFF;

        public uint GetEffectiveUserId()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            return GetEffectiveUserIdNative();
#else
            return uint.MaxValue;
#endif
        }

        public UnixFileMetadata ReadMetadata(string path, bool followSymbolicLinks)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(path), "path must not be empty");

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            if (!System.BitConverter.IsLittleEndian)
            {
                return UnixFileMetadata.Failure(UnsupportedPlatformError);
            }

            UnixStatLayoutResult layoutResult = UnixStatLayoutResolver.Resolve(
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                RuntimeInformation.ProcessArchitecture);
            if (!layoutResult.Success)
            {
                return UnixFileMetadata.Failure(UnsupportedPlatformError);
            }

            // Why one shared buffer: libc stat and lstat write the same platform struct stat.
            // A single maximum-sized buffer keeps their follow/no-follow ABI paths identical;
            // Linux arm64 writes its 128-byte structure into this 144-byte allocation safely.
            byte[] buffer = new byte[UnixNativeStatBufferSize];
            int result = ReadNativeStat(
                path,
                followSymbolicLinks,
                layoutResult.UseDarwinInode64Symbols,
                buffer);
            if (result != 0)
            {
                int errorCode = Marshal.GetLastWin32Error();
                return errorCode == UnixNativeError.NoEntry
                    ? UnixFileMetadata.Missing()
                    : UnixFileMetadata.Failure(errorCode);
            }

            uint mode = ReadUnsignedInteger(buffer, layoutResult.ModeOffset, layoutResult.ModeSize);
            uint ownerUserId = ReadUnsignedInteger(
                buffer,
                layoutResult.OwnerUserIdOffset,
                sizeof(uint));
            return UnixFileMetadata.Existing(
                ToFileKind(mode),
                ownerUserId,
                mode & PermissionMask);
#else
            return UnixFileMetadata.Failure(UnsupportedPlatformError);
#endif
        }

        public UnixNativeOperationResult CreateDirectory(string path, uint mode)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(path), "path must not be empty");

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            // Why this is atomic: mkdir applies the requested 0700 before publishing the
            // directory. A process umask can remove permission bits, but can never widen 0700.
            int result = MakeDirectoryNative(path, mode);
            return result == 0
                ? UnixNativeOperationResult.Successful()
                : UnixNativeOperationResult.Failure(Marshal.GetLastWin32Error());
#else
            return UnixNativeOperationResult.Failure(UnsupportedPlatformError);
#endif
        }

        public UnixNativeOperationResult ChangeMode(string path, uint mode)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(path), "path must not be empty");

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            int result = ChangeModeNative(path, mode);
            return result == 0
                ? UnixNativeOperationResult.Successful()
                : UnixNativeOperationResult.Failure(Marshal.GetLastWin32Error());
#else
            return UnixNativeOperationResult.Failure(UnsupportedPlatformError);
#endif
        }

        private static UnixFileKind ToFileKind(uint mode)
        {
            uint fileType = mode & FileTypeMask;
            switch (fileType)
            {
                case DirectoryType:
                    return UnixFileKind.Directory;
                case RegularFileType:
                    return UnixFileKind.RegularFile;
                case SymbolicLinkType:
                    return UnixFileKind.SymbolicLink;
                case SocketType:
                    return UnixFileKind.Socket;
                default:
                    return UnixFileKind.Other;
            }
        }

        private static uint ReadUnsignedInteger(byte[] buffer, int offset, int size)
        {
            System.Diagnostics.Debug.Assert(buffer != null, "buffer must not be null");
            System.Diagnostics.Debug.Assert(offset >= 0, "offset must not be negative");
            System.Diagnostics.Debug.Assert(size == sizeof(ushort) || size == sizeof(uint), "size must be 2 or 4");
            System.Diagnostics.Debug.Assert(offset + size <= buffer.Length, "field must fit in buffer");

            return size == sizeof(ushort)
                ? System.BitConverter.ToUInt16(buffer, offset)
                : System.BitConverter.ToUInt32(buffer, offset);
        }

        private static int ReadNativeStat(
            string path,
            bool followSymbolicLinks,
            bool useDarwinInode64Symbols,
            byte[] buffer)
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            if (useDarwinInode64Symbols)
            {
                // Why Intel macOS needs decorated symbols: Darwin's headers redirect the
                // 64-bit-inode struct stat ABI to stat$INODE64/lstat$INODE64, while the plain
                // legacy symbols use a different layout on x86_64.
                // Source: https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/cdefs.h
                return followSymbolicLinks
                    ? StatInode64Native(path, buffer)
                    : LStatInode64Native(path, buffer);
            }

            return followSymbolicLinks
                ? StatNative(path, buffer)
                : LStatNative(path, buffer);
#else
            return -1;
#endif
        }

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        [DllImport("libc", EntryPoint = "geteuid", SetLastError = true)]
        private static extern uint GetEffectiveUserIdNative();

        [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
        private static extern int LStatNative(string path, [Out] byte[] buffer);

        [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        private static extern int StatNative(string path, [Out] byte[] buffer);

        [DllImport("libc", EntryPoint = "lstat$INODE64", SetLastError = true)]
        private static extern int LStatInode64Native(string path, [Out] byte[] buffer);

        [DllImport("libc", EntryPoint = "stat$INODE64", SetLastError = true)]
        private static extern int StatInode64Native(string path, [Out] byte[] buffer);

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int ChangeModeNative(string path, uint mode);

        [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
        private static extern int MakeDirectoryNative(string path, uint mode);

#endif
    }

    internal readonly struct UnixStatLayoutResult
    {
        public bool Success { get; }
        public int ModeOffset { get; }
        public int ModeSize { get; }
        public int OwnerUserIdOffset { get; }
        public bool UseDarwinInode64Symbols { get; }

        private UnixStatLayoutResult(
            bool success,
            int modeOffset,
            int modeSize,
            int ownerUserIdOffset,
            bool useDarwinInode64Symbols)
        {
            Success = success;
            ModeOffset = modeOffset;
            ModeSize = modeSize;
            OwnerUserIdOffset = ownerUserIdOffset;
            UseDarwinInode64Symbols = useDarwinInode64Symbols;
        }

        public static UnixStatLayoutResult Supported(
            int modeOffset,
            int modeSize,
            int ownerUserIdOffset,
            bool useDarwinInode64Symbols)
        {
            return new UnixStatLayoutResult(
                true,
                modeOffset,
                modeSize,
                ownerUserIdOffset,
                useDarwinInode64Symbols);
        }

        public static UnixStatLayoutResult Unsupported()
        {
            return new UnixStatLayoutResult(false, 0, 0, 0, false);
        }
    }

    internal static class UnixStatLayoutResolver
    {
        public static UnixStatLayoutResult Resolve(
            bool isMacOS,
            bool isLinux,
            Architecture architecture)
        {
            if (isMacOS && (architecture == Architecture.X64 || architecture == Architecture.Arm64))
            {
                // Why these offsets: Darwin stat64 defines st_mode after st_dev at byte 4 and
                // st_uid at byte 16 for both supported 64-bit Editor architectures.
                // Source: https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/stat.h
                return UnixStatLayoutResult.Supported(
                    4,
                    sizeof(ushort),
                    16,
                    useDarwinInode64Symbols: architecture == Architecture.X64);
            }

            if (isLinux && architecture == Architecture.X64)
            {
                // Why these offsets: glibc x86_64 struct stat places st_mode at byte 24 and
                // st_uid at byte 28 in its 144-byte layout.
                // Source: https://sourceware.org/git/?p=glibc.git;a=blob;f=sysdeps/unix/sysv/linux/x86/bits/struct_stat.h
                return UnixStatLayoutResult.Supported(24, sizeof(uint), 28, useDarwinInode64Symbols: false);
            }

            if (isLinux && architecture == Architecture.Arm64)
            {
                // Why these offsets: glibc AArch64 struct stat places st_mode at byte 16 and
                // st_uid at byte 24 in its 128-byte layout.
                // Source: https://sourceware.org/git/?p=glibc.git;a=blob;f=sysdeps/unix/sysv/linux/bits/struct_stat.h
                return UnixStatLayoutResult.Supported(16, sizeof(uint), 24, useDarwinInode64Symbols: false);
            }

            // Why fail closed: selecting a nearby ABI layout can silently misread owner/mode
            // metadata and turn a validation error into acceptance of an untrusted endpoint.
            return UnixStatLayoutResult.Unsupported();
        }
    }
}
