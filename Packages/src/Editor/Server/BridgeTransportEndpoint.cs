using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    internal enum BridgeTransportKind
    {
        UnixDomainSocket,
        WindowsNamedPipe
    }

    internal sealed class BridgeTransportEndpoint
    {
        private const string LIBC = "libc";
        private const string KERNEL32 = "kernel32.dll";
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        public BridgeTransportKind Kind { get; }
        public string Path { get; }
        public string PipeName { get; }

        private BridgeTransportEndpoint(BridgeTransportKind kind, string path, string pipeName)
        {
            Kind = kind;
            Path = path;
            PipeName = pipeName;
        }

        public static BridgeTransportEndpoint CreateProjectIpc(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(projectRoot), "projectRoot must not be empty");

            string canonicalProjectRoot = CanonicalizeProjectRoot(projectRoot);
            string endpointName = "uLoopMCP-" + CreateEndpointHash(canonicalProjectRoot);
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string pipeName = "uloop-" + endpointName;
                return new BridgeTransportEndpoint(
                    BridgeTransportKind.WindowsNamedPipe,
                    @"\\.\pipe\" + pipeName,
                    pipeName);
            }

            return new BridgeTransportEndpoint(
                BridgeTransportKind.UnixDomainSocket,
                System.IO.Path.Combine("/tmp/uloop", endpointName + ".sock"),
                string.Empty);
        }

        public string DisplayName()
        {
            return Path;
        }

        internal static string CanonicalizeProjectRoot(string projectRoot)
        {
            string fullPath = System.IO.Path.GetFullPath(projectRoot);
            string trimmedPath = TrimTrailingPathSeparators(fullPath);
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return ResolveWindowsFinalPath(trimmedPath);
            }

            return ResolveUnixRealPath(trimmedPath);
        }

        private static string ResolveWindowsFinalPath(string path)
        {
            using SafeFileHandle handle = CreateFile(
                path,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return path;
            }

            StringBuilder builder = new StringBuilder(1024);
            uint length = GetFinalPathNameByHandle(handle, builder, builder.Capacity, 0);
            if (length == 0 || length >= builder.Capacity)
            {
                return path;
            }

            return NormalizeWindowsFinalPath(builder.ToString());
        }

        private static string ResolveUnixRealPath(string path)
        {
            IntPtr resolvedPath = RealPath(path, IntPtr.Zero);
            if (resolvedPath == IntPtr.Zero)
            {
                return path;
            }

            try
            {
                string realPath = Marshal.PtrToStringAnsi(resolvedPath);
                Debug.Assert(!string.IsNullOrWhiteSpace(realPath), "realPath must not be empty");
                return TrimTrailingPathSeparators(realPath);
            }
            finally
            {
                Free(resolvedPath);
            }
        }

        private static string CreateEndpointHash(string canonicalProjectRoot)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalProjectRoot);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            StringBuilder builder = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }

        [DllImport(LIBC, EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPath(string path, IntPtr resolvedPath);

        [DllImport(LIBC, EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);

        private static string NormalizeWindowsFinalPath(string path)
        {
            const string dosDevicePrefix = @"\\?\";
            const string uncDevicePrefix = @"\\?\UNC\";
            if (path.StartsWith(uncDevicePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return TrimTrailingPathSeparators(@"\\" + path.Substring(uncDevicePrefix.Length));
            }

            if (path.StartsWith(dosDevicePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return TrimTrailingPathSeparators(path.Substring(dosDevicePrefix.Length));
            }

            return TrimTrailingPathSeparators(path);
        }

        private static string TrimTrailingPathSeparators(string path)
        {
            string root = System.IO.Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            string trimmedPath = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(trimmedPath))
            {
                return trimmedPath;
            }

            Debug.Assert(!string.IsNullOrEmpty(root), "root must be available when trimming returns empty");
            return root;
        }

        [DllImport(KERNEL32, EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport(KERNEL32, EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle fileHandle,
            StringBuilder filePath,
            int filePathLength,
            uint flags);
    }
}
