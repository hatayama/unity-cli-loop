using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns a failed introduced-type compilation into the response rows of that run, one row per
    /// file the compiler blamed, so the reader sees which source each compiler error belongs to.
    /// </summary>
    internal static class HotReloadIntroducedTypeCompileFailureOutcomes
    {
        private const string ReasonPrefix = "Introduced-type compilation failed: ";

        // Why a declaration the compiler never blamed still gets a row: the batch compiles as one
        // assembly, so a sibling's error leaves this type uncompiled too. Without the row it
        // disappears from the response and reads as a type the reload quietly accepted.
        private const string NotCompiledBecauseSiblingFailedReason =
            "Not compiled: another declaration in the same introduced-type batch failed to compile, "
            + "so this type was not introduced. Fix the failed file and rerun.";

        // Why this has to be spelled out: an introduced type compiles against the compiled
        // assemblies on disk and the retained artifacts, so a member hot reload added, in this
        // reload or an earlier one, is genuinely absent there. The bare compiler error reads as a
        // typo and sends the reader looking for one. Why worded as a condition: only the name is
        // matched, so a missing member of another type can share it. Why splitting is ruled out:
        // reloading the addition first still leaves it outside both, which is the obvious retry.
        private const string AddedMemberInvisibleHint =
            "One or more of the missing members share a name with a hot reload addition, from this "
            + "reload or an earlier one. If the missing member is that addition, an introduced type "
            + "cannot see it: it compiles against the compiled assemblies and earlier introduced "
            + "types only, so reloading the addition first does not help. Run 'uloop compile' to "
            + "make the added members compiled, then rerun.";

        // Why it points at the warning: the enum-member warning of the same run already carries
        // the cast that avoids the member, and repeating the value here would need the enum too.
        private const string AddedEnumMemberInvisibleHint =
            "One or more of the missing members share a name with an enum member this reload adds "
            + "to a compiled enum. Hot reload cannot add an enum member, so no compilation sees it "
            + "until 'uloop compile'. Write the underlying value as the cast the enum-member warning "
            + "in Warnings shows, or run 'uloop compile' to add the member, then rerun.";

        private const string MissingMemberErrorCode = "CS1061:";

        private const string MissingStaticMemberErrorCode = "CS0117:";

        private static readonly string[] MissingMemberErrorCodes =
        {
            MissingMemberErrorCode,
            MissingStaticMemberErrorCode
        };

        public static List<HotReloadIntroducedTypeOutcome> Build(
            HotReloadIntroducedTypeCompilerResult compileResult,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            string targetAssemblyName,
            HotReloadIntroducedTypeAddedMemberNames addedMemberNames)
        {
            if (addedMemberNames == null)
            {
                throw new ArgumentNullException(nameof(addedMemberNames));
            }

            List<HotReloadIntroducedTypeOutcome> rows = new List<HotReloadIntroducedTypeOutcome>();

            // Why one unattributed row: without a diagnostic the failure belongs to the batch and
            // to no single owner, so attributing it to a declaration would be a guess.
            if (compileResult.Diagnostics.Count == 0)
            {
                rows.Add(HotReloadIntroducedTypeOutcome.Failed(
                    string.Empty,
                    targetAssemblyName,
                    string.Empty,
                    ReasonPrefix + compileResult.ErrorMessage));
                return rows;
            }

            List<string> ownerOrder = new List<string>();
            Dictionary<string, List<string>> messagesByOwner = new Dictionary<string, List<string>>();
            List<string> unattributed = new List<string>();
            GroupDiagnostics(compileResult.Diagnostics, ownerOrder, messagesByOwner, unattributed);

            HashSet<string> emittedOwners = new HashSet<string>();
            AppendDescriptorRows(
                descriptors,
                messagesByOwner,
                emittedOwners,
                targetAssemblyName,
                addedMemberNames,
                rows);
            AppendUnknownOwnerRows(
                ownerOrder,
                messagesByOwner,
                emittedOwners,
                targetAssemblyName,
                addedMemberNames,
                rows);
            if (unattributed.Count > 0)
            {
                rows.Add(HotReloadIntroducedTypeOutcome.Failed(
                    string.Empty,
                    targetAssemblyName,
                    string.Empty,
                    ReasonPrefix + string.Join("; ", unattributed)));
            }

            return rows;
        }

        private static void GroupDiagnostics(
            IReadOnlyList<HotReloadIntroducedTypeCompilerDiagnostic> diagnostics,
            List<string> ownerOrder,
            Dictionary<string, List<string>> messagesByOwner,
            List<string> unattributed)
        {
            foreach (HotReloadIntroducedTypeCompilerDiagnostic diagnostic in diagnostics)
            {
                string formatted = FormatDiagnostic(diagnostic);
                if (diagnostic.OwnerProjectRelativePath.Length == 0)
                {
                    unattributed.Add(formatted);
                    continue;
                }

                if (!messagesByOwner.TryGetValue(diagnostic.OwnerProjectRelativePath, out List<string> messages))
                {
                    messages = new List<string>();
                    messagesByOwner.Add(diagnostic.OwnerProjectRelativePath, messages);
                    ownerOrder.Add(diagnostic.OwnerProjectRelativePath);
                }

                messages.Add(formatted);
            }
        }

        // Why the descriptor order and not the diagnostic order: the other IntroducedTypes rows of
        // the response follow the descriptors, so a different order here would read as a different
        // set of types.
        private static void AppendDescriptorRows(
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            Dictionary<string, List<string>> messagesByOwner,
            HashSet<string> emittedOwners,
            string targetAssemblyName,
            HotReloadIntroducedTypeAddedMemberNames addedMemberNames,
            List<HotReloadIntroducedTypeOutcome> rows)
        {
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                string owner = descriptor.OwnerProjectRelativePath ?? string.Empty;

                // Why the first descriptor wins: a compiler diagnostic identifies a file, so two
                // types declared in one file cannot be told apart and share its row.
                if (!emittedOwners.Add(owner))
                {
                    continue;
                }

                if (!messagesByOwner.TryGetValue(owner, out List<string> messages))
                {
                    rows.Add(HotReloadIntroducedTypeOutcome.Failed(
                        descriptor.MetadataName.Value,
                        targetAssemblyName,
                        owner,
                        NotCompiledBecauseSiblingFailedReason));
                    continue;
                }

                rows.Add(HotReloadIntroducedTypeOutcome.Failed(
                    descriptor.MetadataName.Value,
                    targetAssemblyName,
                    owner,
                    BuildReason(messages, addedMemberNames)));
            }
        }

        // Why kept at all: a diagnostic naming a file this run declares no type in still carries
        // the compiler error, and dropping it would leave the reader with nothing to act on.
        private static void AppendUnknownOwnerRows(
            List<string> ownerOrder,
            Dictionary<string, List<string>> messagesByOwner,
            HashSet<string> emittedOwners,
            string targetAssemblyName,
            HotReloadIntroducedTypeAddedMemberNames addedMemberNames,
            List<HotReloadIntroducedTypeOutcome> rows)
        {
            foreach (string owner in ownerOrder)
            {
                if (!emittedOwners.Add(owner))
                {
                    continue;
                }

                rows.Add(HotReloadIntroducedTypeOutcome.Failed(
                    string.Empty,
                    targetAssemblyName,
                    owner,
                    BuildReason(messagesByOwner[owner], addedMemberNames)));
            }
        }

        private static string BuildReason(
            List<string> messages,
            HotReloadIntroducedTypeAddedMemberNames addedMemberNames)
        {
            string reason = ReasonPrefix + string.Join("; ", messages);
            // Why a separate check: Members never holds an enum member, so without it a CS0117 on
            // an added enum member would stay the bare compiler error.
            if (MentionsAddedEnumMember(messages, addedMemberNames.EnumMembers))
            {
                return reason + " " + AddedEnumMemberInvisibleHint;
            }

            if (MentionsAnyOf(messages, addedMemberNames.Members))
            {
                return reason + " " + AddedMemberInvisibleHint;
            }

            return reason;
        }

        private static bool MentionsAnyOf(
            List<string> messages,
            IReadOnlyCollection<string> names)
        {
            if (names.Count == 0)
            {
                return false;
            }

            foreach (string message in messages)
            {
                if (!TryFindMissingMemberDiagnosticCode(message, out int searchStart))
                {
                    continue;
                }

                string member = FindQuotedToken(message, searchStart, 1);
                if (member != null && Contains(names, member))
                {
                    return true;
                }
            }

            return false;
        }

        // Why CS0117 only and the type as well: an enum member is reached through the enum type
        // alone, and a CS1061 or a CS0117 on another type that shares the member name is missing
        // for another reason, so the enum hint would send the reader the wrong way.
        private static bool MentionsAddedEnumMember(
            List<string> messages,
            IReadOnlyCollection<string> qualifiedEnumMembers)
        {
            if (qualifiedEnumMembers.Count == 0)
            {
                return false;
            }

            foreach (string message in messages)
            {
                int codeIndex = message.IndexOf(MissingStaticMemberErrorCode, StringComparison.Ordinal);
                if (codeIndex < 0)
                {
                    continue;
                }

                string typeName = FindQuotedToken(message, codeIndex + MissingStaticMemberErrorCode.Length, 0);
                string member = FindQuotedToken(message, codeIndex + MissingStaticMemberErrorCode.Length, 1);
                if (typeName != null && member != null && NamesEnumMember(qualifiedEnumMembers, typeName, member))
                {
                    return true;
                }
            }

            return false;
        }

        // Why a suffix match on the type: the compiler prints the enum as the source spells it,
        // often without its namespace, while the worker sends the fully qualified display name.
        private static bool NamesEnumMember(
            IReadOnlyCollection<string> qualifiedEnumMembers,
            string typeName,
            string member)
        {
            foreach (string qualified in qualifiedEnumMembers)
            {
                int lastDot = qualified.LastIndexOf('.');
                if (lastDot <= 0
                    || !string.Equals(qualified.Substring(lastDot + 1), member, StringComparison.Ordinal))
                {
                    continue;
                }

                string enumName = qualified.Substring(0, lastDot);
                if (string.Equals(enumName, typeName, StringComparison.Ordinal)
                    || enumName.EndsWith("." + typeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Why the position is reported: the message carries the owner path in front of the
        // diagnostic, and an apostrophe in that path would otherwise be read as the first quoted
        // token of the diagnostic itself.
        private static bool TryFindMissingMemberDiagnosticCode(string message, out int searchStart)
        {
            foreach (string code in MissingMemberErrorCodes)
            {
                int index = message.IndexOf(code, StringComparison.Ordinal);
                if (index >= 0)
                {
                    searchStart = index + code.Length;
                    return true;
                }
            }

            searchStart = 0;
            return false;
        }

        // In these diagnostics the first quoted token names the type that does not hold the member
        // and the second names the missing member.
        private static string FindQuotedToken(string message, int searchStart, int tokenIndex)
        {
            int open = message.IndexOf('\'', searchStart);
            for (int skipped = 0; open >= 0; skipped++)
            {
                int close = message.IndexOf('\'', open + 1);
                if (close < 0)
                {
                    return null;
                }

                if (skipped == tokenIndex)
                {
                    return message.Substring(open + 1, close - open - 1);
                }

                open = message.IndexOf('\'', close + 1);
            }

            return null;
        }

        private static bool Contains(IReadOnlyCollection<string> names, string member)
        {
            foreach (string name in names)
            {
                if (string.Equals(name, member, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatDiagnostic(HotReloadIntroducedTypeCompilerDiagnostic diagnostic)
        {
            if (diagnostic.Line <= 0)
            {
                return diagnostic.Message;
            }

            return diagnostic.OwnerProjectRelativePath
                + "(" + diagnostic.Line + "," + diagnostic.Column + "): "
                + diagnostic.Message;
        }
    }
}
