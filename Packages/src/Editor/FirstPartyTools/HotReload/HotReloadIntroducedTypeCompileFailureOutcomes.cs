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

        public static List<HotReloadIntroducedTypeOutcome> Build(
            HotReloadIntroducedTypeCompilerResult compileResult,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            string targetAssemblyName)
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
            AppendDescriptorRows(descriptors, messagesByOwner, emittedOwners, targetAssemblyName, rows);
            AppendUnknownOwnerRows(ownerOrder, messagesByOwner, emittedOwners, targetAssemblyName, rows);
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
            List<HotReloadIntroducedTypeOutcome> rows)
        {
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                string owner = descriptor.OwnerProjectRelativePath ?? string.Empty;
                if (!messagesByOwner.TryGetValue(owner, out List<string> messages))
                {
                    continue;
                }

                // Why the first descriptor wins: a compiler diagnostic identifies a file, so two
                // types declared in one file cannot be told apart and share its row.
                if (!emittedOwners.Add(owner))
                {
                    continue;
                }

                rows.Add(HotReloadIntroducedTypeOutcome.Failed(
                    descriptor.MetadataName.Value,
                    targetAssemblyName,
                    owner,
                    ReasonPrefix + string.Join("; ", messages)));
            }
        }

        // Why kept at all: a diagnostic naming a file this run declares no type in still carries
        // the compiler error, and dropping it would leave the reader with nothing to act on.
        private static void AppendUnknownOwnerRows(
            List<string> ownerOrder,
            Dictionary<string, List<string>> messagesByOwner,
            HashSet<string> emittedOwners,
            string targetAssemblyName,
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
                    ReasonPrefix + string.Join("; ", messagesByOwner[owner])));
            }
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
