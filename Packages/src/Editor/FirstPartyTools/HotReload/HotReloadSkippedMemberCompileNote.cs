using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Extracts unresolved member names from shim compile errors and matches them to
    /// skipped rows from the same hot-reload run.
    /// </summary>
    internal static class HotReloadSkippedMemberCompileNote
    {
        private const string Cs1061Prefix = "CS1061:";
        private const string Cs0117Prefix = "CS0117:";
        private const string Cs0103Prefix = "CS0103:";
        private const string DefinitionForMarker = "does not contain a definition for '";
        private const string NameMarker = "The name '";

        internal static string[] ExtractUnresolvedMemberNames(IReadOnlyList<string> errorMessages)
        {
            Debug.Assert(errorMessages != null, "errorMessages must not be null.");

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> names = new List<string>();
            for (int index = 0; index < errorMessages.Count; index++)
            {
                string error = errorMessages[index];
                if (string.IsNullOrEmpty(error))
                {
                    continue;
                }

                string name = ExtractNameFromDiagnostic(error);
                if (name == null || !seen.Add(name))
                {
                    continue;
                }

                names.Add(name);
            }

            return names.ToArray();
        }

        internal static string FindSkippedMemberNote(string unresolvedName, TransformWorkerSkippedDto[] skipped)
        {
            Debug.Assert(unresolvedName != null, "unresolvedName must not be null.");

            if (skipped == null)
            {
                return null;
            }

            for (int index = 0; index < skipped.Length; index++)
            {
                TransformWorkerSkippedDto row = skipped[index];
                if (row == null || string.IsNullOrEmpty(row.method))
                {
                    continue;
                }

                string simpleName = ExtractSimpleMethodName(row.method);
                if (string.Equals(simpleName, unresolvedName, StringComparison.Ordinal))
                {
                    return row.reason;
                }
            }

            return null;
        }

        internal static string AppendNotes(
            string composedMessage,
            IReadOnlyList<string> errorMessages,
            TransformWorkerSkippedDto[] skipped)
        {
            Debug.Assert(composedMessage != null, "composedMessage must not be null.");

            string[] unresolvedNames = ExtractUnresolvedMemberNames(errorMessages);
            string message = composedMessage;
            for (int index = 0; index < unresolvedNames.Length; index++)
            {
                string unresolvedName = unresolvedNames[index];
                string reason = FindSkippedMemberNote(unresolvedName, skipped);
                if (reason == null)
                {
                    continue;
                }

                message += "\n" + string.Format(
                    HotReloadConstants.SkippedMemberCompileFailureNoteFormat,
                    unresolvedName,
                    reason);
            }

            return message;
        }

        private static string ExtractNameFromDiagnostic(string error)
        {
            if (error.StartsWith(Cs1061Prefix, StringComparison.Ordinal)
                || error.StartsWith(Cs0117Prefix, StringComparison.Ordinal))
            {
                return ExtractQuotedNameAfter(error, DefinitionForMarker);
            }

            if (error.StartsWith(Cs0103Prefix, StringComparison.Ordinal))
            {
                return ExtractQuotedNameAfter(error, NameMarker);
            }

            return null;
        }

        private static string ExtractQuotedNameAfter(string error, string marker)
        {
            int markerIndex = error.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return null;
            }

            int nameStart = markerIndex + marker.Length;
            int nameEnd = error.IndexOf('\'', nameStart);
            if (nameEnd <= nameStart)
            {
                return null;
            }

            return error.Substring(nameStart, nameEnd - nameStart);
        }

        private static string ExtractSimpleMethodName(string methodLabel)
        {
            int lastDot = methodLabel.LastIndexOf('.');
            int start = lastDot >= 0 ? lastDot + 1 : 0;
            int end = methodLabel.Length;
            int backtick = methodLabel.IndexOf('`', start);
            if (backtick >= 0)
            {
                end = backtick;
            }

            int paren = methodLabel.IndexOf('(', start);
            if (paren >= 0 && paren < end)
            {
                end = paren;
            }

            return methodLabel.Substring(start, end - start);
        }
    }
}
