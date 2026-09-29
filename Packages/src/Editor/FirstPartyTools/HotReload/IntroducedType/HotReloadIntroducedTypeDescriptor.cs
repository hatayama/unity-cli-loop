using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identifies one immutable type declaration owned by a compiled source assembly.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeDescriptor
    {
        public string OriginalAssemblyName { get; }

        public string OriginalAssemblyMvid { get; }

        public HotReloadMetadataTypeName MetadataName { get; }

        public string OwnerProjectRelativePath { get; }

        public string DeclarationFingerprint { get; }

        public string Source { get; }

        /// <summary>
        /// Entry keys of the methods whose bodies <see cref="Source"/> stubs because they call
        /// members a hot reload adds; empty when it stubs none. Only the run that prepares the
        /// artifact reads them, to hold the activation back until each one is patched.
        /// </summary>
        public IReadOnlyList<string> StubbedMethodKeys { get; }

        public HotReloadIntroducedTypeDescriptor(
            string originalAssemblyName,
            string originalAssemblyMvid,
            string metadataName,
            string ownerProjectRelativePath,
            string declarationFingerprint,
            string source,
            IReadOnlyList<string> stubbedMethodKeys = null)
        {
            // BuildIdentity concatenates the first three parts and HasSameDefinition compares the
            // fingerprint, so a missing part would collapse distinct declarations onto one identity
            // key in the registry's mappings or make a changed definition compare as unchanged.
            RequireValue(originalAssemblyName, nameof(originalAssemblyName));
            RequireValue(originalAssemblyMvid, nameof(originalAssemblyMvid));
            RequireValue(metadataName, nameof(metadataName));
            RequireValue(declarationFingerprint, nameof(declarationFingerprint));
            OriginalAssemblyName = originalAssemblyName;
            OriginalAssemblyMvid = originalAssemblyMvid;
            MetadataName = new HotReloadMetadataTypeName(metadataName);
            OwnerProjectRelativePath = ownerProjectRelativePath;
            DeclarationFingerprint = declarationFingerprint;
            Source = source;
            StubbedMethodKeys = CopyStubbedMethodKeys(stubbedMethodKeys);
        }

        // A blank or repeated key would let the activation check count a key it can never match,
        // or one patch for two stubs.
        private static string[] CopyStubbedMethodKeys(IReadOnlyList<string> stubbedMethodKeys)
        {
            if (stubbedMethodKeys == null)
            {
                return Array.Empty<string>();
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string[] copy = new string[stubbedMethodKeys.Count];
            for (int index = 0; index < stubbedMethodKeys.Count; index++)
            {
                string key = stubbedMethodKeys[index];
                RequireValue(key, nameof(stubbedMethodKeys));
                if (!seen.Add(key))
                {
                    throw new ArgumentException("A stubbed method key must not repeat: " + key, nameof(stubbedMethodKeys));
                }

                copy[index] = key;
            }

            return copy;
        }

        private static void RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("An introduced-type descriptor part must not be empty.", parameterName);
            }
        }

        public string BuildIdentity()
        {
            return OriginalAssemblyName + "|" + OriginalAssemblyMvid + "|" + MetadataName.Value;
        }

        public bool HasSameDefinition(HotReloadIntroducedTypeDescriptor other)
        {
            return other != null
                && BuildIdentity() == other.BuildIdentity()
                && OwnerProjectRelativePath == other.OwnerProjectRelativePath
                && DeclarationFingerprint == other.DeclarationFingerprint;
        }
    }
}
