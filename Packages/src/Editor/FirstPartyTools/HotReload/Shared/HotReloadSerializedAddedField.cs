using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// An added field whose declaration carries a serialization attribute, named by the metadata
    /// form of its declaring type so two fields that read the same in C# stay two fields.
    /// </summary>
    /// <remarks>
    /// Why the metadata name is kept and the display name is derived: the display form turns a
    /// nesting step and a namespace segment into the same '.', so it can only be shown, never
    /// used to tell one field from another.
    /// </remarks>
    internal readonly struct HotReloadSerializedAddedField
    {
        public HotReloadMetadataTypeName DeclaringTypeName { get; }

        public string FieldName { get; }

        public HotReloadSerializedAddedField(HotReloadMetadataTypeName declaringTypeName, string fieldName)
        {
            Debug.Assert(!string.IsNullOrEmpty(fieldName), "fieldName must not be null or empty.");

            DeclaringTypeName = declaringTypeName;
            FieldName = fieldName;
        }

        /// <summary>The Type.field name as C# source spells it, for a reader only.</summary>
        public string ToDisplayName()
        {
            return DeclaringTypeName.ToDisplayShortName() + "." + FieldName;
        }

        /// <summary>
        /// A key that tells this field apart from any other the domain holds, given the
        /// project-relative path of the file that declares it.
        /// </summary>
        public string ToIdentityKey(string ownerPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(ownerPath), "ownerPath must not be null or empty.");

            return ownerPath + "\n" + DeclaringTypeName.Value + "\n" + FieldName;
        }
    }
}
