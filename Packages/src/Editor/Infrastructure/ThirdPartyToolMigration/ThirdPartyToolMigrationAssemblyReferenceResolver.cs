using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Resolves asmdef, asmref, and implicit Unity assembly directories for migration planning.
    /// </summary>
    internal static class ThirdPartyToolMigrationAssemblyReferenceResolver
    {
        internal static List<AssemblyReferenceDirectory> CreateAssemblyReferenceDirectories(
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");

            if (asmrefFilePaths.Count == 0)
            {
                return new List<AssemblyReferenceDirectory>();
            }

            Dictionary<string, string> asmdefDirectoriesByReference = CreateAsmdefDirectoryMap(asmdefFilePaths);
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new();
            foreach (string asmrefFilePath in asmrefFilePaths)
            {
                if (!TryReadJsonObjectFromFile(asmrefFilePath, out JObject asmref))
                {
                    continue;
                }

                string reference = asmref["reference"]?.Value<string>() ?? string.Empty;
                if (reference.Length == 0)
                {
                    continue;
                }

                if (!asmdefDirectoriesByReference.TryGetValue(reference, out string targetAssemblyDirectory))
                {
                    continue;
                }

                string sourceDirectory = Path.GetDirectoryName(asmrefFilePath) ?? string.Empty;
                if (sourceDirectory.Length == 0)
                {
                    continue;
                }

                assemblyReferenceDirectories.Add(
                    new AssemblyReferenceDirectory(sourceDirectory, targetAssemblyDirectory));
            }

            return assemblyReferenceDirectories
                .OrderByDescending(assemblyReferenceDirectory => assemblyReferenceDirectory.SourceDirectory.Length)
                .ToList();
        }

        internal static Dictionary<string, string[]> CreateReferencedAssemblyDirectoriesByDirectory(
            List<string> asmdefDirectories)
        {
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");

            List<string> asmdefFilePaths = GetAsmdefFilePathsFromDirectories(asmdefDirectories);
            Dictionary<string, string> asmdefDirectoriesByReference = CreateAsmdefDirectoryMap(asmdefFilePaths);
            Dictionary<string, HashSet<string>> referencedDirectoriesByDirectory = new(StringComparer.Ordinal);
            foreach (string asmdefFilePath in asmdefFilePaths)
            {
                string assemblyDirectory = Path.GetDirectoryName(asmdefFilePath) ?? string.Empty;
                if (assemblyDirectory.Length == 0)
                {
                    continue;
                }

                if (!TryReadJsonObjectFromFile(asmdefFilePath, out JObject asmdef))
                {
                    continue;
                }

                if (asmdef["references"] is not JArray references)
                {
                    continue;
                }

                foreach (JToken referenceToken in references)
                {
                    string reference = referenceToken.Value<string>() ?? string.Empty;
                    if (!asmdefDirectoriesByReference.TryGetValue(reference, out string referencedAssemblyDirectory))
                    {
                        continue;
                    }

                    AddAssemblyScopedNames(
                        referencedDirectoriesByDirectory,
                        assemblyDirectory,
                        new[] { referencedAssemblyDirectory });
                }
            }

            return CreateAssemblyScopedNamesByDirectory(referencedDirectoriesByDirectory);
        }

        internal static List<string> GetAsmdefFilePathsFromDirectories(List<string> asmdefDirectories)
        {
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");

            List<string> asmdefFilePaths = new();
            foreach (string asmdefDirectory in asmdefDirectories)
            {
                if (!Directory.Exists(asmdefDirectory))
                {
                    continue;
                }

                asmdefFilePaths.AddRange(Directory.GetFiles(
                    asmdefDirectory,
                    "*.asmdef",
                    SearchOption.TopDirectoryOnly));
            }

            return asmdefFilePaths
                .OrderBy(asmdefFilePath => asmdefFilePath, StringComparer.Ordinal)
                .ToList();
        }

        internal static async Task<List<AssemblyReferenceDirectory>> CreateAssemblyReferenceDirectoriesAsync(
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            MigrationProgressCounter progressCounter,
            CancellationToken ct)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");
            Debug.Assert(progressCounter != null, "progressCounter must not be null");

            if (asmrefFilePaths.Count == 0)
            {
                return new List<AssemblyReferenceDirectory>();
            }

            Dictionary<string, string> asmdefDirectoriesByReference =
                await CreateAsmdefDirectoryMapAsync(asmdefFilePaths, sourceFileCache, progressCounter, ct);
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new();
            foreach (string asmrefFilePath in asmrefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return assemblyReferenceDirectories
                        .OrderByDescending(
                            assemblyReferenceDirectory => assemblyReferenceDirectory.SourceDirectory.Length)
                        .ToList();
                }

                if (!TryReadJsonObjectFromCache(asmrefFilePath, sourceFileCache, out JObject asmref))
                {
                    await progressCounter.ReportProcessedItemAsync(ct);
                    continue;
                }

                await progressCounter.ReportProcessedItemAsync(ct);
                string reference = asmref["reference"]?.Value<string>() ?? string.Empty;
                if (reference.Length == 0)
                {
                    continue;
                }

                if (!asmdefDirectoriesByReference.TryGetValue(reference, out string targetAssemblyDirectory))
                {
                    continue;
                }

                string sourceDirectory = Path.GetDirectoryName(asmrefFilePath) ?? string.Empty;
                if (sourceDirectory.Length == 0)
                {
                    continue;
                }

                assemblyReferenceDirectories.Add(
                    new AssemblyReferenceDirectory(sourceDirectory, targetAssemblyDirectory));
            }

            return assemblyReferenceDirectories
                .OrderByDescending(assemblyReferenceDirectory => assemblyReferenceDirectory.SourceDirectory.Length)
                .ToList();
        }

        internal static Dictionary<string, string> CreateAsmdefDirectoryMap(List<string> asmdefFilePaths)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");

            Dictionary<string, string> asmdefDirectoriesByReference = new(StringComparer.Ordinal);
            foreach (string asmdefFilePath in asmdefFilePaths)
            {
                string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? string.Empty;
                if (asmdefDirectory.Length == 0)
                {
                    continue;
                }

                if (!TryReadJsonObjectFromFile(asmdefFilePath, out JObject asmdef))
                {
                    continue;
                }

                string assemblyName = asmdef["name"]?.Value<string>() ?? string.Empty;
                AddAsmdefDirectoryReference(asmdefDirectoriesByReference, assemblyName, asmdefDirectory);
                AddAsmdefDirectoryReference(
                    asmdefDirectoriesByReference,
                    ReadAsmdefGuidReference(asmdefFilePath),
                    asmdefDirectory);
            }

            return asmdefDirectoriesByReference;
        }

        internal static async Task<Dictionary<string, string>> CreateAsmdefDirectoryMapAsync(
            List<string> asmdefFilePaths,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            MigrationProgressCounter progressCounter,
            CancellationToken ct)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");
            Debug.Assert(progressCounter != null, "progressCounter must not be null");

            Dictionary<string, string> asmdefDirectoriesByReference = new(StringComparer.Ordinal);
            foreach (string asmdefFilePath in asmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return asmdefDirectoriesByReference;
                }

                string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? string.Empty;
                if (asmdefDirectory.Length == 0)
                {
                    await progressCounter.ReportProcessedItemAsync(ct);
                    continue;
                }

                if (!TryReadJsonObjectFromCache(asmdefFilePath, sourceFileCache, out JObject asmdef))
                {
                    await progressCounter.ReportProcessedItemAsync(ct);
                    continue;
                }

                await progressCounter.ReportProcessedItemAsync(ct);
                string assemblyName = asmdef["name"]?.Value<string>() ?? string.Empty;
                AddAsmdefDirectoryReference(asmdefDirectoriesByReference, assemblyName, asmdefDirectory);
                AddAsmdefDirectoryReference(
                    asmdefDirectoriesByReference,
                    ReadAsmdefGuidReference(asmdefFilePath),
                    asmdefDirectory);
            }

            return asmdefDirectoriesByReference;
        }

        internal static bool TryReadJsonObjectFromFile(string filePath, out JObject jsonObject)
        {
            return TryReadJsonObjectForMigration(filePath, File.ReadAllText, out jsonObject);
        }

        internal static bool TryReadJsonObjectFromCache(
            string filePath,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            out JObject jsonObject)
        {
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");

            return TryReadJsonObjectForMigration(filePath, sourceFileCache.ReadAllText, out jsonObject);
        }

        internal static bool TryReadJsonObjectForMigration(
            string filePath,
            Func<string, string> readAllText,
            out JObject jsonObject)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(readAllText != null, "readAllText must not be null");

            try
            {
                jsonObject = JObject.Parse(readAllText(filePath));
                return true;
            }
            catch (Exception ex) when (IsSkippableAssemblyJsonReadException(ex))
            {
                UnityEngine.Debug.LogWarning(
                    $"[UnityCliLoop] Skipping unreadable or malformed assembly JSON at {filePath}: {ex.Message}");
                jsonObject = null;
                return false;
            }
        }

        internal static bool IsSkippableAssemblyJsonReadException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            return ex is JsonException ||
                   ex is IOException ||
                   ex is UnauthorizedAccessException;
        }

        internal static void AddAsmdefDirectoryReference(
            Dictionary<string, string> asmdefDirectoriesByReference,
            string reference,
            string asmdefDirectory)
        {
            Debug.Assert(asmdefDirectoriesByReference != null, "asmdefDirectoriesByReference must not be null");
            Debug.Assert(reference != null, "reference must not be null");
            Debug.Assert(!string.IsNullOrEmpty(asmdefDirectory), "asmdefDirectory must not be null or empty");

            if (reference.Length == 0 || asmdefDirectoriesByReference.ContainsKey(reference))
            {
                return;
            }

            asmdefDirectoriesByReference.Add(reference, asmdefDirectory);
        }

        internal static string ReadAsmdefGuidReference(string asmdefFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");

            string metaPath = asmdefFilePath + ".meta";
            if (!File.Exists(metaPath))
            {
                return string.Empty;
            }

            foreach (string line in File.ReadLines(metaPath))
            {
                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("guid:", StringComparison.Ordinal))
                {
                    continue;
                }

                string guid = trimmedLine.Substring("guid:".Length).Trim();
                return guid.Length == 0 ? string.Empty : $"GUID:{guid}";
            }

            return string.Empty;
        }

        internal static string FindNearestAssemblyDirectory(
            string csharpFilePath,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string csharpDirectory = Path.GetDirectoryName(csharpFilePath) ?? string.Empty;
            string matchedAssemblyDirectory = string.Empty;
            int matchedSourceDirectoryLength = -1;
            foreach (string asmdefDirectory in asmdefDirectories)
            {
                if (!IsSameOrChildPath(csharpDirectory, asmdefDirectory) ||
                    asmdefDirectory.Length <= matchedSourceDirectoryLength)
                {
                    continue;
                }

                matchedAssemblyDirectory = asmdefDirectory;
                matchedSourceDirectoryLength = asmdefDirectory.Length;
            }

            foreach (AssemblyReferenceDirectory assemblyReferenceDirectory in assemblyReferenceDirectories)
            {
                string sourceDirectory = assemblyReferenceDirectory.SourceDirectory;
                if (!IsSameOrChildPath(csharpDirectory, sourceDirectory) ||
                    sourceDirectory.Length <= matchedSourceDirectoryLength)
                {
                    continue;
                }

                matchedAssemblyDirectory = assemblyReferenceDirectory.TargetAssemblyDirectory;
                matchedSourceDirectoryLength = sourceDirectory.Length;
            }

            if (matchedAssemblyDirectory.Length > 0)
            {
                return matchedAssemblyDirectory;
            }

            return GetImplicitAssemblyDirectory(csharpFilePath, projectRoot);
        }

        internal static string GetImplicitAssemblyDirectory(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            bool isEditorAssemblyPath = IsEditorAssemblyPath(csharpFilePath, projectRoot);
            bool isFirstPassAssemblyPath = IsFirstPassAssemblyPath(csharpFilePath, projectRoot);
            string implicitAssemblyDirectoryName = GetImplicitAssemblyDirectoryName(
                isEditorAssemblyPath,
                isFirstPassAssemblyPath);
            return Path.Combine(projectRoot, implicitAssemblyDirectoryName);
        }

        internal static string GetImplicitAssemblyDirectoryName(
            bool isEditorAssemblyPath,
            bool isFirstPassAssemblyPath)
        {
            if (isEditorAssemblyPath && isFirstPassAssemblyPath)
            {
                return ThirdPartyToolMigrationFileServiceConstants.ImplicitFirstPassEditorAssemblyDirectoryName;
            }

            if (isFirstPassAssemblyPath)
            {
                return ThirdPartyToolMigrationFileServiceConstants.ImplicitFirstPassRuntimeAssemblyDirectoryName;
            }

            return isEditorAssemblyPath
                ? ThirdPartyToolMigrationFileServiceConstants.ImplicitEditorAssemblyDirectoryName
                : ThirdPartyToolMigrationFileServiceConstants.ImplicitRuntimeAssemblyDirectoryName;
        }

        internal static bool IsEditorAssemblyPath(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string[] pathSegments = GetRelativePathSegments(csharpFilePath, projectRoot);
            return pathSegments.Any(
                pathSegment => string.Equals(pathSegment, "Editor", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsFirstPassAssemblyPath(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string[] pathSegments = GetRelativePathSegments(csharpFilePath, projectRoot);
            if (pathSegments.Length < 2 ||
                !string.Equals(pathSegments[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(pathSegments[1], "Plugins", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pathSegments[1], "Standard Assets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pathSegments[1], "Pro Standard Assets", StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] GetRelativePathSegments(string filePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string relativePath = filePath.StartsWith(projectRoot, StringComparison.Ordinal)
                ? filePath.Substring(projectRoot.Length)
                : filePath;
            char[] separators =
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            };
            return relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        }

        internal static bool IsSameOrChildPath(string childPath, string parentPath)
        {
            Debug.Assert(childPath != null, "childPath must not be null");
            Debug.Assert(parentPath != null, "parentPath must not be null");

            if (string.Equals(childPath, parentPath, StringComparison.Ordinal))
            {
                return true;
            }

            string parentWithSeparator = parentPath.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return childPath.StartsWith(parentWithSeparator, StringComparison.Ordinal);
        }
    }
}
