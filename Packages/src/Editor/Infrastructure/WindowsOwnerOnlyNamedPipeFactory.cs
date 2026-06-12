using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Creates Windows named pipe server streams whose DACL grants access to the owning user only.
    /// </summary>
    // Why: on Unity's Mono runtime the managed security APIs are unusable for this:
    // WindowsIdentity.GetCurrent().User throws NotImplementedException, the
    // NamedPipeServerStream(..., PipeSecurity) overload silently ignores the supplied ACL
    // (the pipe keeps the default DACL that any local user can open), and
    // PipeStream.GetAccessControl crashes the editor natively. The only reliable way to
    // restrict the pipe is to create it through CreateNamedPipeW with an explicit security
    // descriptor and wrap the native handle in a managed stream.
    internal static class WindowsOwnerOnlyNamedPipeFactory
    {
        private const int TOKEN_QUERY = 0x0008;
        private const int TOKEN_INFORMATION_CLASS_TOKEN_USER = 1;
        private const int SDDL_REVISION_1 = 1;
        private const uint PIPE_ACCESS_DUPLEX = 0x00000003;
        private const uint PIPE_TYPE_BYTE_READMODE_BYTE_WAIT = 0x00000000;
        private const uint PIPE_UNLIMITED_INSTANCES = 255;

        /// <summary>
        /// Builds an SDDL descriptor that grants FullControl to the current user's SID only.
        /// </summary>
        // Why: "P" marks the DACL protected and the single ACE covers exactly the owning user,
        // so every other local principal (other interactive/RDP users on a shared host) is
        // denied at the transport boundary.
        internal static string BuildCurrentUserOnlySddl()
        {
            SecurityIdentifier currentUser = GetCurrentUserSid();
            return $"D:P(A;;FA;;;{currentUser.Value})";
        }

        /// <summary>
        /// Creates a byte-mode duplex named pipe server instance protected by the given SDDL descriptor.
        /// </summary>
        // Why: the handle is created without FILE_FLAG_OVERLAPPED and wrapped with isAsync: false
        // because Mono's managed wrap of an overlapped pipe handle hangs in WaitForConnection.
        // The synchronous wait matches how the accept loop already runs (a dedicated Task.Run thread).
        internal static NamedPipeServerStream CreateServer(string pipeName, string sddl)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(pipeName), "pipeName must not be empty");
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(sddl), "sddl must not be empty");

            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SDDL_REVISION_1, out IntPtr descriptor, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Security descriptor conversion failed for the named pipe DACL.");
            }

            try
            {
                SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                    lpSecurityDescriptor = descriptor,
                    bInheritHandle = false,
                };
                IntPtr handle = CreateNamedPipeW(
                    @"\\.\pipe\" + pipeName,
                    PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_BYTE_READMODE_BYTE_WAIT,
                    PIPE_UNLIMITED_INSTANCES,
                    0,
                    0,
                    0,
                    ref attributes);
                if (handle == new IntPtr(-1))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateNamedPipeW failed for pipe '{pipeName}'.");
                }

                SafePipeHandle safeHandle = new SafePipeHandle(handle, ownsHandle: true);
                return new NamedPipeServerStream(PipeDirection.InOut, isAsync: false, isConnected: false, safePipeHandle: safeHandle);
            }
            finally
            {
                LocalFree(descriptor);
            }
        }

        private static SecurityIdentifier GetCurrentUserSid()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out IntPtr token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed while resolving the current user SID.");
            }

            try
            {
                GetTokenInformation(token, TOKEN_INFORMATION_CLASS_TOKEN_USER, IntPtr.Zero, 0, out int requiredLength);
                IntPtr buffer = Marshal.AllocHGlobal(requiredLength);
                try
                {
                    if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS_TOKEN_USER, buffer, requiredLength, out _))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation failed while resolving the current user SID.");
                    }

                    // TOKEN_USER begins with a SID_AND_ATTRIBUTES whose first field is the PSID.
                    IntPtr sidPointer = Marshal.ReadIntPtr(buffer);
                    return new SecurityIdentifier(sidPointer);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string sddl,
            int revision,
            out IntPtr descriptor,
            out int descriptorSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateNamedPipeW(
            string name,
            uint openMode,
            uint pipeMode,
            uint maxInstances,
            uint outBufferSize,
            uint inBufferSize,
            uint defaultTimeout,
            ref SECURITY_ATTRIBUTES securityAttributes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
