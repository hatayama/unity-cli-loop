using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Where the source snapshot of a compiled assembly lives under the project, and what each
    /// captured source is called there. The writer and every reader of a snapshot take the paths
    /// from here; nothing here touches the file system.
    /// </summary>
    internal static class HotReloadSourceSnapshotLayout
    {
        // The suffix of an assembly directory that is still being written; only a Move makes it complete.
        internal const string IncompleteDirectorySuffix = ".tmp";

        internal static string Root(string projectRoot)
        {
            return Path.Combine(projectRoot, HotReloadConstants.SourceSnapshotRelativeDirectory);
        }

        internal static string AssemblyDirectoryName(string assemblyName, string mvid)
        {
            return assemblyName + "-" + mvid;
        }

        internal static string AssemblyDirectory(string projectRoot, string assemblyName, string mvid)
        {
            return Path.Combine(Root(projectRoot), AssemblyDirectoryName(assemblyName, mvid));
        }

        internal static string SourceFileName(string slashNormalizedProjectRelativePath)
        {
            return HashProjectRelativePath(slashNormalizedProjectRelativePath) + ".cs";
        }

        internal static string SourcePath(string assemblyDirectory, string projectRelativePath)
        {
            return Path.Combine(assemblyDirectory, SourceFileName(projectRelativePath.Replace('\\', '/')));
        }

        private static string HashProjectRelativePath(string slashNormalizedProjectRelativePath)
        {
            Debug.Assert(
                slashNormalizedProjectRelativePath != null,
                "slashNormalizedProjectRelativePath must not be null.");

            string hashInput = slashNormalizedProjectRelativePath;
            // Why lowercase only on Windows: PDB document matching is OrdinalIgnoreCase there, so
            // a case-only path difference must hash to the same snapshot filename. Unix filesystems
            // can be case-sensitive, so leave the path bytes unchanged on those platforms.
            if (Path.DirectorySeparatorChar == '\\')
            {
                hashInput = hashInput.ToLowerInvariant();
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(hashInput);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(utf8);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
