using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Explains a missing-member diagnostic whose name is a member hot reload added: dynamic code
    /// compiles against the compiled assemblies only, so an added member is invisible here by design.
    /// </summary>
    internal static class AddedMemberDiagnosticHint
    {
        private const string MissingInstanceMemberErrorCode = "CS1061";
        private const string MissingStaticMemberErrorCode = "CS0117";

        // An added member referred to without its declaring type is reported as an unknown
        // identifier rather than a missing member, because no such name is in scope at all.
        private const string NameNotInContextErrorCode = "CS0103";

        private const string CompileSuggestion =
            "Run 'uloop compile' to make the added member compiled, then rerun";

        private const string CompiledMemberSuggestion =
            "Read the state through a member that was already compiled instead of the added one";

        /// <summary>
        /// Builds the hint and suggestions for a diagnostic that names an active added member.
        /// Returns false when the diagnostic is unrelated, which leaves the existing hints in place.
        /// </summary>
        internal static bool TryBuild(
            string errorCode,
            string message,
            IReadOnlyList<string> activeAddedMemberNames,
            out string hint,
            out List<string> suggestions)
        {
            hint = string.Empty;
            suggestions = null;

            if (!IsMissingMemberErrorCode(errorCode))
            {
                return false;
            }

            if (activeAddedMemberNames == null || activeAddedMemberNames.Count == 0)
            {
                return false;
            }

            string name = ExtractMemberName(errorCode, message);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!ContainsExactly(activeAddedMemberNames, name))
            {
                return false;
            }

            hint = BuildHint(name);
            suggestions = new List<string> { CompileSuggestion, CompiledMemberSuggestion };
            return true;
        }

        private static bool IsMissingMemberErrorCode(string errorCode)
        {
            return string.Equals(errorCode, MissingInstanceMemberErrorCode, StringComparison.Ordinal)
                || string.Equals(errorCode, MissingStaticMemberErrorCode, StringComparison.Ordinal)
                || string.Equals(errorCode, NameNotInContextErrorCode, StringComparison.Ordinal);
        }

        // Why the quoted phrase differs by code: CS1061 and CS0117 quote the declaring type first and
        // the member second, while CS0103 quotes the bare name and no type at all.
        private static string ExtractMemberName(string errorCode, string message)
        {
            if (string.Equals(errorCode, NameNotInContextErrorCode, StringComparison.Ordinal))
            {
                return CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(message);
            }

            return CompilationDiagnosticMessageParser.ExtractMemberNameFromMessage(message);
        }

        // The comparison is exact because hot reload publishes bare member names: a case-insensitive
        // or prefix match would claim an unrelated typo as a hot-reload consequence.
        private static bool ContainsExactly(IReadOnlyList<string> activeAddedMemberNames, string name)
        {
            foreach (string addedMemberName in activeAddedMemberNames)
            {
                if (string.Equals(addedMemberName, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // The wording stops at "matches": the name alone cannot prove the snippet meant the added
        // member rather than a member it misspelled.
        private static string BuildHint(string name)
        {
            return $"'{name}' matches a member hot reload added, which is active now. An added member "
                + "is not visible to the compilation of a dynamic-code snippet, which compiles against "
                + "the compiled assemblies only, and it is not visible through reflection either. Run "
                + "'uloop compile' to make the added member compiled, then rerun, or read the state "
                + "through a member that was already compiled.";
        }
    }
}
