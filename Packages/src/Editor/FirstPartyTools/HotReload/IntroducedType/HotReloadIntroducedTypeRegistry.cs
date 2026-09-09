using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps prepared artifacts isolated until activation publishes them for runtime resolution.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeRegistry
    {
        private readonly Dictionary<string, HotReloadIntroducedTypeArtifact> activeByTypeIdentity =
            new Dictionary<string, HotReloadIntroducedTypeArtifact>(StringComparer.Ordinal);
        private readonly Dictionary<string, HotReloadIntroducedTypeArtifact> activeByAssemblyIdentity =
            new Dictionary<string, HotReloadIntroducedTypeArtifact>(StringComparer.Ordinal);
        private readonly HashSet<HotReloadIntroducedTypeArtifact> preparedArtifacts =
            new HashSet<HotReloadIntroducedTypeArtifact>();

        // AppDomain.AssemblyResolve runs on whichever thread requested the failed bind, so the
        // resolver reads this state off the main thread while activation mutates it on the main
        // thread. Every access takes this gate, and the resolver takes the same one, so the two
        // never see a dictionary mid-write.
        private readonly object gate = new object();

        internal object Gate => gate;

        public int ActiveCount
        {
            get
            {
                lock (gate)
                {
                    return activeByAssemblyIdentity.Count;
                }
            }
        }

        // The number of active introduced types, which is not the number of active artifacts:
        // one artifact carries every type of the batch that compiled it. A report that counted
        // artifacts would name two types and total them as one.
        public int ActiveTypeCount
        {
            get
            {
                lock (gate)
                {
                    return activeByTypeIdentity.Count;
                }
            }
        }

        public int PreparedCount
        {
            get
            {
                lock (gate)
                {
                    return preparedArtifacts.Count;
                }
            }
        }

        public bool TryFindActive(
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            out HotReloadIntroducedTypeArtifact artifact)
        {
            artifact = null;
            if (descriptors == null || descriptors.Count == 0 || descriptors[0] == null)
            {
                return false;
            }

            lock (gate)
            {
                if (!activeByTypeIdentity.TryGetValue(descriptors[0].BuildIdentity(), out artifact))
                {
                    artifact = null;
                    return false;
                }
            }

            if (artifact.MatchesDefinitionSet(descriptors))
            {
                return true;
            }

            artifact = null;
            return false;
        }

        public bool TryFindActiveDescriptor(
            HotReloadIntroducedTypeDescriptor descriptor,
            out HotReloadIntroducedTypeArtifact artifact)
        {
            artifact = null;
            if (descriptor == null)
            {
                return false;
            }

            lock (gate)
            {
                if (!activeByTypeIdentity.TryGetValue(descriptor.BuildIdentity(), out artifact))
                {
                    return false;
                }
            }

            if (artifact.MatchesDescriptor(descriptor))
            {
                return true;
            }

            artifact = null;
            return false;
        }

        public void RegisterPrepared(HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            lock (gate)
            {
                if (activeByAssemblyIdentity.TryGetValue(
                    artifact.AssemblyFullName,
                    out HotReloadIntroducedTypeArtifact activeArtifact)
                    && ReferenceEquals(activeArtifact, artifact))
                {
                    throw new InvalidOperationException("An active introduced-type artifact cannot be prepared again.");
                }

                // A second Prepared registration of the same artifact would make one
                // DiscardPrepared drop the membership the other registration still relies on, so
                // the later Activate would be rejected as never prepared.
                if (!preparedArtifacts.Add(artifact))
                {
                    throw new InvalidOperationException("An introduced-type artifact is already prepared.");
                }
            }
        }

        public void Activate(HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            lock (gate)
            {
                if (!preparedArtifacts.Contains(artifact))
                {
                    throw new InvalidOperationException("Only a prepared introduced-type artifact can be activated.");
                }

                ValidateActivation(artifact);
                activeByAssemblyIdentity.Add(artifact.AssemblyFullName, artifact);
                foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
                {
                    activeByTypeIdentity.Add(descriptor.BuildIdentity(), artifact);
                }

                preparedArtifacts.Remove(artifact);
            }
        }

        public void DiscardPrepared(HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                return;
            }

            lock (gate)
            {
                preparedArtifacts.Remove(artifact);
            }
        }

        /// <summary>
        /// A snapshot of every active introduced type, for reporting what this domain holds.
        /// </summary>
        /// <remarks>
        /// Why a copy taken under the gate: a caller that reported straight off the live
        /// dictionaries would race a concurrent activation, and the resolver takes the same gate.
        /// Ordered so a report built from it is stable across runs.
        /// </remarks>
        public IReadOnlyList<HotReloadIntroducedTypeDescriptor> DescribeActive()
        {
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>();
            lock (gate)
            {
                foreach (HotReloadIntroducedTypeArtifact artifact in activeByAssemblyIdentity.Values)
                {
                    descriptors.AddRange(artifact.Descriptors);
                }
            }

            descriptors.Sort(CompareForReport);
            return descriptors;
        }

        /// <summary>
        /// Answers whether a compiled assembly still owns a type this domain introduced.
        /// </summary>
        public bool HasActiveTypesForOriginalAssembly(string originalAssemblyName)
        {
            if (string.IsNullOrEmpty(originalAssemblyName))
            {
                return false;
            }

            lock (gate)
            {
                foreach (HotReloadIntroducedTypeArtifact artifact in activeByAssemblyIdentity.Values)
                {
                    foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
                    {
                        if (string.Equals(descriptor.OriginalAssemblyName, originalAssemblyName, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Collects the active artifacts whose types belong to one generation of one compiled
        /// assembly, which is the only generation a run may normalize its declarations back to.
        /// </summary>
        public IReadOnlyList<HotReloadIntroducedTypeArtifact> CollectActiveArtifactsForTarget(
            string originalAssemblyName,
            string originalAssemblyMvid)
        {
            List<HotReloadIntroducedTypeArtifact> matches = new List<HotReloadIntroducedTypeArtifact>();
            if (string.IsNullOrEmpty(originalAssemblyName) || string.IsNullOrEmpty(originalAssemblyMvid))
            {
                return matches;
            }

            lock (gate)
            {
                foreach (HotReloadIntroducedTypeArtifact artifact in activeByAssemblyIdentity.Values)
                {
                    if (HasTypeOfTarget(artifact, originalAssemblyName, originalAssemblyMvid))
                    {
                        matches.Add(artifact);
                    }
                }
            }

            return matches;
        }

        private static bool HasTypeOfTarget(
            HotReloadIntroducedTypeArtifact artifact,
            string originalAssemblyName,
            string originalAssemblyMvid)
        {
            foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
            {
                if (string.Equals(descriptor.OriginalAssemblyName, originalAssemblyName, StringComparison.Ordinal)
                    && string.Equals(descriptor.OriginalAssemblyMvid, originalAssemblyMvid, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryResolveActiveAssembly(string requestedAssemblyFullName, out HotReloadIntroducedTypeArtifact artifact)
        {
            lock (gate)
            {
                return activeByAssemblyIdentity.TryGetValue(requestedAssemblyFullName, out artifact);
            }
        }

        private static int CompareForReport(
            HotReloadIntroducedTypeDescriptor left,
            HotReloadIntroducedTypeDescriptor right)
        {
            int byAssembly = string.Compare(
                left.OriginalAssemblyName,
                right.OriginalAssemblyName,
                StringComparison.Ordinal);
            if (byAssembly != 0)
            {
                return byAssembly;
            }

            return string.Compare(left.MetadataName.Value, right.MetadataName.Value, StringComparison.Ordinal);
        }

        private void ValidateActivation(HotReloadIntroducedTypeArtifact artifact)
        {
            if (activeByAssemblyIdentity.ContainsKey(artifact.AssemblyFullName))
            {
                throw new InvalidOperationException("An introduced-type artifact assembly is already active.");
            }

            HashSet<string> descriptorIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
            {
                if (descriptor == null)
                {
                    throw new InvalidOperationException("An introduced-type artifact cannot contain a null descriptor.");
                }

                string identity = descriptor.BuildIdentity();
                if (!descriptorIdentities.Add(identity))
                {
                    throw new InvalidOperationException("An introduced-type artifact cannot contain duplicate descriptors.");
                }

                if (activeByTypeIdentity.ContainsKey(identity))
                {
                    throw new InvalidOperationException("An introduced-type identity is already active.");
                }
            }
        }
    }
}
