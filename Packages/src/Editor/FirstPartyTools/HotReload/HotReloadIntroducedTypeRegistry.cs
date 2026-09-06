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

                preparedArtifacts.Add(artifact);
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

        public bool TryResolveActiveAssembly(string requestedAssemblyFullName, out HotReloadIntroducedTypeArtifact artifact)
        {
            lock (gate)
            {
                return activeByAssemblyIdentity.TryGetValue(requestedAssemblyFullName, out artifact);
            }
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
