using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Roslyn-independent catalog of dangerous API types and members.
    /// Data stays shared because both preload and post-load validators must make identical decisions.
    /// </summary>
    public static class DangerousApiCatalog
    {
        private static readonly HashSet<string> DangerousTypes = new(System.StringComparer.Ordinal)
        {
            "System.IO.FileInfo",
            "System.IO.DirectoryInfo",
            "System.IO.FileSystemWatcher",
            "System.Net.Http.HttpClient",
            "System.Net.WebClient",
            "System.Net.WebRequest",
            "System.Net.Sockets.Socket",
            "System.Net.Sockets.TcpClient",
            "System.Net.Sockets.TcpListener",
            "System.Net.Sockets.UdpClient",
            "System.Net.Sockets.NetworkStream",
            "System.Net.Dns",
            "System.Net.IPAddress",
            "System.Net.HttpWebRequest",
            "System.Net.Security.SslStream",
            "System.Net.WebSockets.ClientWebSocket",
            "System.Net.WebSockets.WebSocket",
            "System.Net.Mail.SmtpClient",
            "System.Net.NetworkInformation.Ping",
            "System.Net.Http.HttpClientHandler",
            "System.Net.Http.HttpMessageHandler",
            "System.Net.Http.SocketsHttpHandler",
            "System.Diagnostics.ProcessStartInfo",
            "System.Web.HttpContext",
            "System.Web.HttpRequest",
            "System.Web.HttpResponse",
            "UnityEngine.Networking.UnityWebRequest",
            "UnityEngine.Networking.NetworkTransport",
            "System.Data.SqlClient.SqlConnection",
            "System.Data.SqlClient.SqlCommand",
            "System.Data.DataSet",
            "System.Runtime.Remoting.RemotingConfiguration",
            "System.Runtime.Remoting.RemotingServices",
            "System.Security.Cryptography.X509Certificates.X509Certificate",
            "System.Security.Cryptography.X509Certificates.X509Store"
        };

        private static readonly Dictionary<string, HashSet<string>> DangerousMembers = new(System.StringComparer.Ordinal)
        {
            ["System.IO.File"] = new() { "Delete", "WriteAllText", "WriteAllBytes", "Replace", "OpenWrite", "AppendAllText", "AppendText", "SetAttributes", "SetCreationTime", "SetCreationTimeUtc", "SetLastAccessTime", "SetLastAccessTimeUtc", "SetLastWriteTime", "SetLastWriteTimeUtc" },
            ["System.IO.Directory"] = new() { "Delete", "SetCurrentDirectory" },
            ["System.Diagnostics.Process"] = new() { "Start", "Kill" },
            ["System.Reflection.Assembly"] = new() { "Load", "LoadFrom", "LoadFile", "LoadWithPartialName", "GetType" },
            ["System.Type"] = new() { "GetType" },
            ["System.Activator"] = new() { "CreateComInstanceFrom" },
            ["UnityEditor.AssetDatabase"] = new() { "DeleteAsset" },
            ["UnityEditor.FileUtil"] = new() { "DeleteFileOrDirectory" },
            ["System.Environment"] = new() { "Exit", "FailFast" },
            ["System.Threading.Thread"] = new() { "Abort", "Suspend", "Resume" },
            ["io.github.hatayama.UnityCliLoop.DynamicCodeSecurityManager"] = new() { "InitializeFromSettings" },
            ["io.github.hatayama.UnityCliLoop.ULoopSettings"] = new() { "SetDynamicCodeSecurityLevel" }
        };

        public static bool IsDangerousType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return false;
            }

            return DangerousTypes.Contains(fullTypeName);
        }

        public static bool IsDangerousApi(string fullTypeName, string memberName)
        {
            if (string.IsNullOrEmpty(fullTypeName) || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            if (DangerousTypes.Contains(fullTypeName))
            {
                return true;
            }

            if (DangerousMembers.TryGetValue(fullTypeName, out HashSet<string> members))
            {
                return members.Contains(memberName);
            }

            return false;
        }

        public static IEnumerable<string> EnumerateDangerousTypes()
        {
            return DangerousTypes;
        }

        public static IEnumerable<KeyValuePair<string, IReadOnlyCollection<string>>> EnumerateDangerousMembers()
        {
            foreach (KeyValuePair<string, HashSet<string>> entry in DangerousMembers)
            {
                yield return new KeyValuePair<string, IReadOnlyCollection<string>>(
                    entry.Key,
                    new List<string>(entry.Value));
            }
        }
    }
}
