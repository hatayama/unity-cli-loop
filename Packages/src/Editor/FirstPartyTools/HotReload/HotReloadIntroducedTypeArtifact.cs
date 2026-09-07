using System;
using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Retains the loaded assembly and emitted files of one introduced-type preparation batch.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeArtifact
    {
        public Assembly Assembly { get; }

        public string DllPath { get; }

        public string PdbPath { get; }

        public IReadOnlyList<HotReloadIntroducedTypeDescriptor> Descriptors { get; }

        public string AssemblyFullName => Assembly.FullName;

        public HotReloadIntroducedTypeArtifact(
            Assembly assembly,
            string dllPath,
            string pdbPath,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            if (descriptors.Count == 0)
            {
                throw new ArgumentException("Introduced-type descriptors must not be empty.", nameof(descriptors));
            }

            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    throw new ArgumentException("Introduced-type descriptors must not contain null.", nameof(descriptors));
                }
            }

            Assembly = assembly;
            DllPath = dllPath;
            PdbPath = pdbPath;
            Descriptors = new List<HotReloadIntroducedTypeDescriptor>(descriptors).AsReadOnly();
        }

        public bool MatchesDefinitionSet(IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count != Descriptors.Count)
            {
                return false;
            }

            bool[] matched = new bool[Descriptors.Count];
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                if (!TryMatchUnmatchedDescriptor(descriptor, matched))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryMatchUnmatchedDescriptor(
            HotReloadIntroducedTypeDescriptor descriptor,
            bool[] matched)
        {
            for (int index = 0; index < Descriptors.Count; index++)
            {
                if (matched[index] || !Descriptors[index].HasSameDefinition(descriptor))
                {
                    continue;
                }

                matched[index] = true;
                return true;
            }

            return false;
        }

        public bool MatchesDescriptor(HotReloadIntroducedTypeDescriptor descriptor)
        {
            foreach (HotReloadIntroducedTypeDescriptor existing in Descriptors)
            {
                if (existing.HasSameDefinition(descriptor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
