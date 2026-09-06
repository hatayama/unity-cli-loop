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
            OriginalAssemblyName = originalAssemblyName;
            OriginalAssemblyMvid = originalAssemblyMvid;
            MetadataName = metadataName;
            OwnerProjectRelativePath = ownerProjectRelativePath;
            DeclarationFingerprint = declarationFingerprint;
            Source = source;
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
