using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identifies one immutable type declaration owned by a compiled source assembly.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeDescriptor
    {
        public string OriginalAssemblyName { get; }

        public string OriginalAssemblyMvid { get; }

        public string MetadataName { get; }

        public string OwnerProjectRelativePath { get; }

        public string DeclarationFingerprint { get; }

        public string Source { get; }

        public HotReloadIntroducedTypeDescriptor(
            string originalAssemblyName,
            string originalAssemblyMvid,
            string metadataName,
            string ownerProjectRelativePath,
            string declarationFingerprint,
            string source)
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
            MetadataName = metadataName;
            OwnerProjectRelativePath = ownerProjectRelativePath;
            DeclarationFingerprint = declarationFingerprint;
            Source = source;
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
            return OriginalAssemblyName + "|" + OriginalAssemblyMvid + "|" + MetadataName;
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
