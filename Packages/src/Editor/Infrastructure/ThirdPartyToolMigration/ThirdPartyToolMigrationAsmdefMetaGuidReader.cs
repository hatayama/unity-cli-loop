using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads Unity asmdef GUID references from generated .meta files for asmref resolution.
    /// </summary>
    internal static class ThirdPartyToolMigrationAsmdefMetaGuidReader
    {
        internal static string ReadAsmdefGuidReferenceFromAsmdefPath(string asmdefFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");

            string metaPath = asmdefFilePath + ".meta";
            if (!File.Exists(metaPath))
            {
                return string.Empty;
            }

            return ReadAsmdefGuidReferenceFromMetaFile(metaPath, File.ReadLines);
        }

        internal static string ReadAsmdefGuidReferenceFromMetaFile(
            string metaPath,
            Func<string, IEnumerable<string>> readLines)
        {
            Debug.Assert(!string.IsNullOrEmpty(metaPath), "metaPath must not be null or empty");
            Debug.Assert(readLines != null, "readLines must not be null");

            try
            {
                foreach (string line in readLines(metaPath))
                {
                    string trimmedLine = line.Trim();
                    if (!trimmedLine.StartsWith("guid:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string guid = trimmedLine.Substring("guid:".Length).Trim();
                    return guid.Length == 0 ? string.Empty : $"GUID:{guid}";
                }
            }
            catch (Exception ex) when (IsSkippableAssemblyMetaReadException(ex))
            {
                UnityEngine.Debug.LogWarning(
                    $"[UnityCliLoop] Skipping unreadable asmdef meta file at {metaPath}: {ex.Message}");
                return string.Empty;
            }

            return string.Empty;
        }

        private static bool IsSkippableAssemblyMetaReadException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            return ex is IOException ||
                   ex is UnauthorizedAccessException;
        }
    }
}
