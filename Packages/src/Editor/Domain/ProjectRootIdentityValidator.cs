using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Carries the result data produced by Project Root Identity Validation behavior.
    /// </summary>
    public sealed class ProjectRootIdentityValidationResult
    {
        public bool IsValid { get; }

        public string ErrorMessage { get; }

        private ProjectRootIdentityValidationResult(bool isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static ProjectRootIdentityValidationResult Success()
        {
            return new ProjectRootIdentityValidationResult(true, null);
        }

        public static ProjectRootIdentityValidationResult Failure(string errorMessage)
        {
            return new ProjectRootIdentityValidationResult(false, errorMessage);
        }
    }

    /// <summary>
    /// Validates Project Root Identity data before the owning workflow continues.
    /// </summary>
    public static class ProjectRootIdentityValidator
    {
        public static ProjectRootIdentityValidationResult Validate(
            string expectedProjectRoot,
            string actualProjectRoot)
        {
            if (string.IsNullOrWhiteSpace(expectedProjectRoot))
            {
                return ProjectRootIdentityValidationResult.Failure("Invalid x-uloop metadata: expectedProjectRoot is required.");
            }

            if (string.IsNullOrWhiteSpace(actualProjectRoot))
            {
                return ProjectRootIdentityValidationResult.Failure("Fast project validation is unavailable. Restart Unity CLI Loop and retry.");
            }

            if (!string.Equals(expectedProjectRoot, actualProjectRoot, StringComparison.Ordinal))
            {
                return ProjectRootIdentityValidationResult.Failure("Connected Unity instance belongs to a different project.");
            }

            return ProjectRootIdentityValidationResult.Success();
        }
    }

    /// <summary>
    /// Provides Project Root Canonicalizer behavior for Unity CLI Loop.
    /// </summary>
    public static class ProjectRootCanonicalizer
    {
        private const string LIBC = "libc";
        private const string KERNEL32 = "kernel32.dll";
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        public static string Canonicalize(string projectRoot)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(projectRoot), "projectRoot must not be empty");

            string fullPath = Path.GetFullPath(projectRoot);
            string trimmedPath = TrimTrailingPathSeparators(fullPath);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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

            StringBuilder builder = new(1024);
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
                System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(realPath), "realPath must not be empty");
                return TrimTrailingPathSeparators(realPath);
            }
            finally
            {
                Free(resolvedPath);
            }
        }

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
            string root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(trimmedPath))
            {
                return trimmedPath;
            }

            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(root), "root must be available when trimming returns empty");
            return root;
        }

        [DllImport(LIBC, EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPath(string path, IntPtr resolvedPath);

        [DllImport(LIBC, EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);

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
