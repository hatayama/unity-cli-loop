using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects current Unity Console errors that point at Assembly Definition or Assembly Reference assets.
    /// </summary>
    public sealed class AssemblyDefinitionConsoleErrorValidationService
    {
        private const int MaxDisplayedIssueCount = 10;
        private const string GuidReferencePrefix = "GUID:";
        private readonly Func<string, string, bool> _isCurrentImportError;

        private static readonly Regex AssemblyDefinitionAssetPathRegex = new(
            "(?<path>(?:Assets|Packages)/[^\\r\\n]*?\\.(?:asmdef|asmref))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex ReferencesBlockRegex = new(
            "\"references\"\\s*:\\s*\\[(?<references>[^\\]]*)\\]",
            RegexOptions.Compiled | RegexOptions.Singleline
        );

        private static readonly Regex QuotedValueRegex = new(
            "\"(?<value>[^\"]+)\"",
            RegexOptions.Compiled
        );

        private static readonly Regex AssemblyNameRegex = new(
            "\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"",
            RegexOptions.Compiled
        );

        private static readonly Regex AssemblyReferenceRegex = new(
            "\"reference\"\\s*:\\s*\"(?<reference>[^\"]+)\"",
            RegexOptions.Compiled
        );

        private static readonly Regex DuplicateAssemblyNameMessageRegex = new(
            "^Assembly with name '(?<name>[^']+)'",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Creates a validator that confirms Console importer errors against the current asset state.
        /// </summary>
        public AssemblyDefinitionConsoleErrorValidationService(
            Func<string, string, bool> isCurrentImportError = null)
        {
            _isCurrentImportError = isCurrentImportError ?? IsCurrentImportError;
        }

        /// <summary>
        /// Finds Assembly Definition and Assembly Reference errors from the current Unity Console.
        /// </summary>
        public AssemblyDefinitionConsoleErrorResult FindCurrentErrors()
        {
            LogRetrievalService retrievalService = new();
            LogDisplayDto logData = retrievalService.GetLogs(UnityCliLoopLogType.Error);
            LogEntryDto[] logEntries = logData?.LogEntries ?? Array.Empty<LogEntryDto>();
            UnityCliLoopConsoleLogEntry[] entries = new UnityCliLoopConsoleLogEntry[logEntries.Length];

            for (int i = 0; i < logEntries.Length; i++)
            {
                LogEntryDto entry = logEntries[i];
                entries[i] = new UnityCliLoopConsoleLogEntry(
                    entry.LogType,
                    entry.Message,
                    entry.StackTrace);
            }

            return FindErrors(entries);
        }

        /// <summary>
        /// Finds Assembly Definition and Assembly Reference errors from a Console snapshot.
        /// </summary>
        public AssemblyDefinitionConsoleErrorResult FindErrors(UnityCliLoopConsoleLogEntry[] entries)
        {
            Debug.Assert(entries != null, "entries must not be null");

            List<AssemblyDefinitionConsoleError> errors = new();
            HashSet<string> seenKeys = new(StringComparer.Ordinal);

            foreach (UnityCliLoopConsoleLogEntry entry in entries)
            {
                if (!IsErrorEntry(entry))
                {
                    continue;
                }

                string message = entry.Message ?? string.Empty;
                string stackTrace = entry.StackTrace ?? string.Empty;
                string searchableText = $"{message}\n{stackTrace}";
                Match pathMatch = AssemblyDefinitionAssetPathRegex.Match(searchableText);
                if (!pathMatch.Success)
                {
                    continue;
                }

                string file = pathMatch.Groups["path"].Value;
                if (!_isCurrentImportError(file, searchableText))
                {
                    continue;
                }

                string issueMessage = string.IsNullOrWhiteSpace(message)
                    ? searchableText.Trim()
                    : message;
                string key = $"{file}\n{issueMessage}";
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                errors.Add(new AssemblyDefinitionConsoleError(issueMessage, file, 0));
            }

            return new AssemblyDefinitionConsoleErrorResult(errors.ToArray());
        }

        /// <summary>
        /// Checks whether a Console snapshot entry belongs to the error family.
        /// </summary>
        private static bool IsErrorEntry(UnityCliLoopConsoleLogEntry entry)
        {
            return entry != null &&
                   string.Equals(entry.Type, UnityCliLoopLogType.Error, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether the referenced asset still satisfies the importer error reported in Console.
        /// </summary>
        private static bool IsCurrentImportError(string assetPath, string message)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                return false;
            }

            if (assetPath.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
            {
                return HasMalformedAssemblyReference(assetPath) ||
                       (ContainsText(message, "Assembly Reference") && HasMissingAssemblyReferenceTarget(assetPath)) ||
                       HasAssetImportLog(assetPath);
            }

            if (assetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                return HasMalformedAssemblyDefinition(assetPath) ||
                       HasDuplicateReferences(assetPath) ||
                       HasMultipleAssemblyDefinitionFilesInFolder(assetPath) ||
                       HasDuplicateAssemblyName(assetPath, message) ||
                       HasAssetImportLog(assetPath);
            }

            return false;
        }

        /// <summary>
        /// Checks whether an asmdef still declares duplicate reference strings.
        /// </summary>
        private static bool HasDuplicateReferences(string assetPath)
        {
            string source = ReadAssetText(assetPath);
            Match referencesBlock = ReferencesBlockRegex.Match(source);
            if (!referencesBlock.Success)
            {
                return false;
            }

            HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
            MatchCollection referenceMatches = QuotedValueRegex.Matches(referencesBlock.Groups["references"].Value);
            foreach (Match referenceMatch in referenceMatches)
            {
                string reference = referenceMatch.Groups["value"].Value;
                string referenceIdentity = ResolveAssemblyReferenceIdentity(reference);
                if (!references.Add(referenceIdentity))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether an asmdef is missing the minimum JSON shape Unity's importer requires.
        /// </summary>
        private static bool HasMalformedAssemblyDefinition(string assetPath)
        {
            string source = ReadAssetText(assetPath).Trim();
            if (string.IsNullOrEmpty(source))
            {
                return true;
            }

            if (!source.StartsWith("{", StringComparison.Ordinal) ||
                !source.EndsWith("}", StringComparison.Ordinal))
            {
                return true;
            }

            Match nameMatch = AssemblyNameRegex.Match(source);
            return !nameMatch.Success;
        }

        /// <summary>
        /// Checks whether a directory still contains more than one assembly definition source file.
        /// </summary>
        private static bool HasMultipleAssemblyDefinitionFilesInFolder(string assetPath)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            string directoryPath = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                return false;
            }

            int asmdefCount = Directory.GetFiles(directoryPath, "*.asmdef", SearchOption.TopDirectoryOnly).Length;
            int asmrefCount = Directory.GetFiles(directoryPath, "*.asmref", SearchOption.TopDirectoryOnly).Length;
            return asmdefCount + asmrefCount > 1;
        }

        /// <summary>
        /// Checks whether the duplicate assembly name from Console still exists in current asmdef assets.
        /// </summary>
        private static bool HasDuplicateAssemblyName(string assetPath, string message)
        {
            string expectedName = ReadAssemblyDefinitionName(assetPath);
            Match messageMatch = DuplicateAssemblyNameMessageRegex.Match(message);
            if (messageMatch.Success)
            {
                expectedName = messageMatch.Groups["name"].Value;
            }

            if (string.IsNullOrWhiteSpace(expectedName))
            {
                return false;
            }

            int matchCount = 0;
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (string guid in asmdefGuids)
            {
                string asmdefPath = AssetDatabase.GUIDToAssetPath(guid);
                string assemblyName = ReadAssemblyDefinitionName(asmdefPath);
                if (!string.Equals(assemblyName, expectedName, StringComparison.Ordinal))
                {
                    continue;
                }

                matchCount++;
                if (matchCount > 1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether an asmref is missing its reference value.
        /// </summary>
        private static bool HasMalformedAssemblyReference(string assetPath)
        {
            string source = ReadAssetText(assetPath);
            Match referenceMatch = AssemblyReferenceRegex.Match(source);
            return !referenceMatch.Success;
        }

        /// <summary>
        /// Checks whether an asmref still points at a missing assembly definition target.
        /// </summary>
        private static bool HasMissingAssemblyReferenceTarget(string assetPath)
        {
            string source = ReadAssetText(assetPath);
            Match referenceMatch = AssemblyReferenceRegex.Match(source);
            if (!referenceMatch.Success)
            {
                return false;
            }

            string reference = referenceMatch.Groups["reference"].Value;
            return !AssemblyReferenceTargetExists(reference);
        }

        /// <summary>
        /// Resolves an asmdef reference string to the assembly identity Unity uses for duplicate checks.
        /// </summary>
        private static string ResolveAssemblyReferenceIdentity(string reference)
        {
            if (!reference.StartsWith(GuidReferencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return reference;
            }

            string guid = reference.Substring(GuidReferencePrefix.Length);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                return reference;
            }

            return ReadAssemblyDefinitionName(path);
        }

        /// <summary>
        /// Checks whether an asmref reference string resolves to a current asmdef.
        /// </summary>
        private static bool AssemblyReferenceTargetExists(string reference)
        {
            if (reference.StartsWith(GuidReferencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string guid = reference.Substring(GuidReferencePrefix.Length);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                return path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase);
            }

            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (string guid in asmdefGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string assemblyName = ReadAssemblyDefinitionName(assetPath);
                if (string.Equals(assemblyName, reference, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the assembly name declared by an asmdef file.
        /// </summary>
        private static string ReadAssemblyDefinitionName(string assetPath)
        {
            string source = ReadAssetText(assetPath);
            Match nameMatch = AssemblyNameRegex.Match(source);
            if (nameMatch.Success)
            {
                return nameMatch.Groups["name"].Value;
            }

            return Path.GetFileNameWithoutExtension(assetPath);
        }

        /// <summary>
        /// Reads project-relative asset text from disk.
        /// </summary>
        private static string ReadAssetText(string assetPath)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(absolutePath);
        }

        /// <summary>
        /// Converts a Unity project-relative asset path to an absolute file path.
        /// </summary>
        private static string ToAbsolutePath(string assetPath)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(assetPath), "assetPath must not be null or empty");

            if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                string packagePath = ToPackageAbsolutePath(assetPath);
                if (!string.IsNullOrEmpty(packagePath))
                {
                    return packagePath;
                }
            }

            return Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), assetPath);
        }

        /// <summary>
        /// Converts a Package Manager virtual asset path to the package file path on disk.
        /// </summary>
        private static string ToPackageAbsolutePath(string assetPath)
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (packageInfo == null ||
                string.IsNullOrEmpty(packageInfo.assetPath) ||
                string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                return string.Empty;
            }

            if (!assetPath.StartsWith(packageInfo.assetPath, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            string relativePath = assetPath.Substring(packageInfo.assetPath.Length)
                .TrimStart('/', '\\');
            return Path.Combine(packageInfo.resolvedPath, relativePath);
        }

        /// <summary>
        /// Checks whether Unity still exposes an error import log for the asset.
        /// </summary>
        private static bool HasAssetImportLog(string assetPath)
        {
            ImportLog importLog = AssetImporter.GetImportLog(assetPath);
            if (importLog == null)
            {
                return false;
            }

            foreach (ImportLog.ImportLogEntry entry in importLog.logEntries)
            {
                if ((entry.flags & ImportLogFlags.Error) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks for case-insensitive marker text.
        /// </summary>
        private static bool ContainsText(string value, string marker)
        {
            return value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Creates the compile failure message shown when Assembly Definition or Assembly Reference errors are present.
        /// </summary>
        internal static string CreateFailureMessage(AssemblyDefinitionConsoleError[] errors)
        {
            Debug.Assert(errors != null, "errors must not be null");

            string details = string.Join(
                "\n",
                errors
                    .Take(MaxDisplayedIssueCount)
                    .Select(error => string.IsNullOrWhiteSpace(error.File)
                        ? $"- {error.Message}"
                        : $"- {error.File}: {error.Message}")
            );

            return $"{UnityCliLoopConstants.ERROR_MESSAGE_ASSEMBLY_DEFINITION_IMPORT_ERROR}\n{details}";
        }
    }

    /// <summary>
    /// Immutable Console error snapshot for one Assembly Definition or Assembly Reference issue.
    /// </summary>
    public sealed class AssemblyDefinitionConsoleError
    {
        public string Message { get; }
        public string File { get; }
        public int Line { get; }

        public AssemblyDefinitionConsoleError(string message, string file, int line)
        {
            Message = message ?? string.Empty;
            File = file ?? string.Empty;
            Line = line;
        }
    }

    /// <summary>
    /// Immutable result for Assembly Definition and Assembly Reference Console error detection.
    /// </summary>
    public sealed class AssemblyDefinitionConsoleErrorResult
    {
        public AssemblyDefinitionConsoleError[] Errors { get; }
        public bool HasErrors => Errors.Length > 0;
        public string Message { get; }

        public AssemblyDefinitionConsoleErrorResult(AssemblyDefinitionConsoleError[] errors)
        {
            Errors = errors ?? Array.Empty<AssemblyDefinitionConsoleError>();
            Message = HasErrors
                ? AssemblyDefinitionConsoleErrorValidationService.CreateFailureMessage(Errors)
                : null;
        }
    }
}
