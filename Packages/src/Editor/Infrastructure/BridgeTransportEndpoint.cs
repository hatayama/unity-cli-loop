using System.Security.Cryptography;
using System.Text;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal enum BridgeTransportKind
    {
        UnixDomainSocket,
        WindowsNamedPipe
    }

    /// <summary>
    /// Represents the endpoint used by Bridge Transport communication.
    /// </summary>
    internal sealed class BridgeTransportEndpoint
    {
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
            string endpointName = "UnityCliLoop-" + CreateEndpointHash(canonicalProjectRoot);
            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
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

        public static string CanonicalizeProjectRoot(string projectRoot)
        {
            return ProjectRootCanonicalizer.Canonicalize(projectRoot);
        }

        private static string CreateEndpointHash(string canonicalProjectRoot)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalProjectRoot);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            StringBuilder builder = new(16);
            for (int i = 0; i < 8; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }

    }
}
