using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Resolves asmdef "GUID:xxxx" references to assembly names from a map the caller builds once,
    /// and exposes the project asmdef enumeration that map is built from.
    /// Why: the asmdef policy tests used to rebuild the map (or rescan the whole project, Library/
    /// included) for every single reference, which cost several seconds per test.
    /// </summary>
    internal static class AsmdefGuidNameMap
    {
        private const string GuidReferencePrefix = "GUID:";
        private const string MetaGuidKey = "guid:";

        /// <summary>
        /// Builds the guid to assembly name map from every asmdef under Assets/ and Packages/src/.
        /// Library/ is deliberately never scanned: it holds no project asmdef sources.
        /// </summary>
        internal static Dictionary<string, string> Build()
        {
            string[] asmdefPaths = ReadProjectAsmdefPaths();
            Dictionary<string, string> guidToAssemblyNameMap = new();

            foreach (string asmdefPath in asmdefPaths)
            {
                string metaPath = asmdefPath + ".meta";
                if (!File.Exists(metaPath))
                {
                    continue;
                }

                string guid = ReadMetaGuid(metaPath);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                guidToAssemblyNameMap[guid] = ReadAsmdefName(asmdefPath);
            }

            return guidToAssemblyNameMap;
        }

        /// <summary>
        /// Returns the mapped assembly name, or the raw reference when it is a plain name reference
        /// or an unknown guid.
        /// </summary>
        internal static string Resolve(string reference, Dictionary<string, string> guidToAssemblyNameMap)
        {
            if (!reference.StartsWith(GuidReferencePrefix, StringComparison.Ordinal))
            {
                return reference;
            }

            string guid = reference.Substring(GuidReferencePrefix.Length);
            if (!guidToAssemblyNameMap.ContainsKey(guid))
            {
                return reference;
            }

            return guidToAssemblyNameMap[guid];
        }

        /// <summary>
        /// Absolute paths of every asmdef under Assets/ and Packages/src/.
        /// </summary>
        internal static string[] ReadProjectAsmdefPaths()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<string> asmdefPaths = new();
            string assetsPath = Path.Combine(projectRoot, "Assets");
            string packagesSrcPath = Path.Combine(projectRoot, "Packages", "src");

            if (Directory.Exists(assetsPath))
            {
                asmdefPaths.AddRange(Directory.GetFiles(assetsPath, "*.asmdef", SearchOption.AllDirectories));
            }

            if (Directory.Exists(packagesSrcPath))
            {
                asmdefPaths.AddRange(Directory.GetFiles(packagesSrcPath, "*.asmdef", SearchOption.AllDirectories));
            }

            return asmdefPaths.ToArray();
        }

        /// <summary>
        /// Assembly name declared by an asmdef, or empty when the file declares none.
        /// </summary>
        internal static string ReadAsmdefName(string asmdefPath)
        {
            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            return asmdef["name"]?.Value<string>() ?? string.Empty;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            string[] lines = File.ReadAllLines(metaPath);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith(MetaGuidKey, StringComparison.Ordinal))
                {
                    continue;
                }

                return trimmedLine.Substring(MetaGuidKey.Length).Trim();
            }

            return string.Empty;
        }
    }
}
