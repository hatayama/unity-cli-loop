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
        // assemblies on disk, so a member hot reload added earlier is genuinely absent there. The
        // bare compiler error reads as a typo and sends the reader looking for one.
        private const string AddedMemberInvisibleHint =
            "One or more of the missing members were added by hot reload (Added rows) and are not "
            + "visible to the compilation of an introduced type, which compiles against the "
            + "compiled assemblies only. Run 'uloop compile' to make the added members compiled, "
            + "then rerun.";

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
            IReadOnlyCollection<string> activeAddedMemberNames)
        {
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
                activeAddedMemberNames,
                rows);
            AppendUnknownOwnerRows(
                ownerOrder,
                messagesByOwner,
                emittedOwners,
                targetAssemblyName,
                activeAddedMemberNames,
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
            IReadOnlyCollection<string> activeAddedMemberNames,
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
                    BuildReason(messages, activeAddedMemberNames)));
            }
        }

        // Why kept at all: a diagnostic naming a file this run declares no type in still carries
        // the compiler error, and dropping it would leave the reader with nothing to act on.
        private static void AppendUnknownOwnerRows(
            List<string> ownerOrder,
            Dictionary<string, List<string>> messagesByOwner,
            HashSet<string> emittedOwners,
            string targetAssemblyName,
            IReadOnlyCollection<string> activeAddedMemberNames,
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
                    BuildReason(messagesByOwner[owner], activeAddedMemberNames)));
            }
        }

        private static string BuildReason(
            List<string> messages,
            IReadOnlyCollection<string> activeAddedMemberNames)
        {
            string reason = ReasonPrefix + string.Join("; ", messages);
            if (!MentionsAnActiveAddedMember(messages, activeAddedMemberNames))
            {
                return reason;
            }

            return reason + " " + AddedMemberInvisibleHint;
        }

        private static bool MentionsAnActiveAddedMember(
            List<string> messages,
            IReadOnlyCollection<string> activeAddedMemberNames)
        {
            if (activeAddedMemberNames == null || activeAddedMemberNames.Count == 0)
            {
                return false;
            }

            foreach (string message in messages)
            {
                if (!TryFindMissingMemberDiagnosticCode(message, out int searchStart))
                {
                    continue;
                }

                string member = FindSecondQuotedToken(message, searchStart);
                if (member != null && Contains(activeAddedMemberNames, member))
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

        // The missing member is the second quoted token of these diagnostics; the first one names
        // the type that does not hold it.
        private static string FindSecondQuotedToken(string message, int searchStart)
        {
            int firstOpen = message.IndexOf('\'', searchStart);
            if (firstOpen < 0)
            {
                return null;
            }

            int firstClose = message.IndexOf('\'', firstOpen + 1);
            if (firstClose < 0)
            {
                return null;
            }

            int secondOpen = message.IndexOf('\'', firstClose + 1);
            if (secondOpen < 0)
            {
                return null;
            }

            int secondClose = message.IndexOf('\'', secondOpen + 1);
            if (secondClose < 0)
            {
                return null;
            }

            return message.Substring(secondOpen + 1, secondClose - secondOpen - 1);
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
