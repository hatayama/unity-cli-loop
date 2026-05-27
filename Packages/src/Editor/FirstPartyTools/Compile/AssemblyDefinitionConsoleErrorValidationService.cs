using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        private static readonly Regex AssemblyDefinitionAssetPathRegex = new(
            "(?<path>(?:Assets|Packages)/[^\\r\\n()]*?\\.(?:asmdef|asmref))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly string[] AssemblyDefinitionImporterErrorMarkers =
        {
            "Assembly has duplicate references:",
            "Assembly Reference",
            "Assembly Definition",
            "Assembly with name '",
            "assembly definition files"
        };

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

                if (!IsAssemblyDefinitionImporterError(searchableText))
                {
                    continue;
                }

                string file = pathMatch.Groups["path"].Value;
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
        /// Checks whether a Console error has Unity's Assembly Definition or Assembly Reference importer wording.
        /// </summary>
        private static bool IsAssemblyDefinitionImporterError(string message)
        {
            foreach (string marker in AssemblyDefinitionImporterErrorMarkers)
            {
                if (message.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
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
