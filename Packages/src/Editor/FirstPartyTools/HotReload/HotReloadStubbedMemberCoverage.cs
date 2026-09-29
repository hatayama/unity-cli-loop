using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds the activation of a prepared artifact back until this run patches every body the
    /// artifact stubs. A stubbed body throws instead of running the source, and the artifact's
    /// types can be called the moment it is activated, so activating it with a stub left in place
    /// would introduce a type that fails where its source works.
    /// </summary>
    internal static class HotReloadStubbedMemberCoverage
    {
        // Why the sibling types fail too: the batch is one assembly that is activated as a whole,
        // so holding one type back holds every type of it back.
        private const string SiblingNotCoveredReason =
            "Not introduced: another declaration in the same introduced-type batch has a body this "
            + "reload did not patch, so this type was not introduced either. Fix that declaration and rerun.";

        /// <summary>
        /// The rows of every type the artifact holds when a body it stubs has no resolved entry in
        /// this run, or an empty list when every stubbed body is patched. A null
        /// <paramref name="preparedFiles"/> is a run with no entry at all.
        /// </summary>
        internal static List<HotReloadIntroducedTypeOutcome> FindUncoveredTypes(
            HotReloadIntroducedTypeArtifact artifact,
            IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            HashSet<string> patchedKeys = CollectResolvedEntryKeys(preparedFiles);
            Dictionary<HotReloadIntroducedTypeDescriptor, List<string>> uncoveredLabels =
                new Dictionary<HotReloadIntroducedTypeDescriptor, List<string>>();
            foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
            {
                List<string> labels = CollectUncoveredLabels(descriptor, patchedKeys);
                if (labels.Count > 0)
                {
                    uncoveredLabels[descriptor] = labels;
                }
            }

            List<HotReloadIntroducedTypeOutcome> rows = new List<HotReloadIntroducedTypeOutcome>();
            if (uncoveredLabels.Count == 0)
            {
                return rows;
            }

            foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
            {
                string reason = uncoveredLabels.TryGetValue(descriptor, out List<string> labels)
                    ? BuildUncoveredReason(labels)
                    : SiblingNotCoveredReason;
                rows.Add(
                    HotReloadIntroducedTypeOutcome.Failed(
                        descriptor.MetadataName.Value,
                        descriptor.OriginalAssemblyName,
                        descriptor.OwnerProjectRelativePath,
                        reason));
            }

            return rows;
        }

        // Only a resolved file is applied, so an entry of a file the group skipped or could not
        // resolve patches nothing and must not count.
        private static HashSet<string> CollectResolvedEntryKeys(IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            if (preparedFiles == null)
            {
                return keys;
            }

            foreach (HotReloadPreparedGroupFile prepared in preparedFiles)
            {
                if (prepared.Kind != HotReloadGroupFilePreparationKind.Resolved)
                {
                    continue;
                }

                foreach (TransformWorkerEntryDto entry in prepared.Entries)
                {
                    keys.Add(HotReloadMethodKeys.BuildMethodKey(entry));
                }
            }

            return keys;
        }

        private static List<string> CollectUncoveredLabels(
            HotReloadIntroducedTypeDescriptor descriptor,
            HashSet<string> patchedKeys)
        {
            List<string> labels = new List<string>();
            foreach (string stubbedMethodKey in descriptor.StubbedMethodKeys)
            {
                if (!patchedKeys.Contains(stubbedMethodKey))
                {
                    labels.Add(ToMethodLabel(stubbedMethodKey));
                }
            }

            return labels;
        }

        // The label is the key in reflection form: the separator before the method name becomes
        // '.', and every nested-type separator becomes '+'. Only a parameter type can carry one,
        // because an introduced type is never nested.
        private static string ToMethodLabel(string methodKey)
        {
            return methodKey.Replace("::", ".").Replace('/', '+');
        }

        private static string BuildUncoveredReason(List<string> labels)
        {
            if (labels.Count == 1)
            {
                return "Not introduced: " + labels[0]
                    + " calls members that a hot reload added, so its body runs only once this reload "
                    + "patches it in, and this reload did not. Fix what kept it from being patched and "
                    + "rerun, or run 'uloop compile' to apply this edit.";
            }

            return "Not introduced: " + string.Join(", ", labels)
                + " call members that a hot reload added, so their bodies run only once this reload "
                + "patches them in, and this reload did not. Fix what kept them from being patched and "
                + "rerun, or run 'uloop compile' to apply this edit.";
        }
    }
}
