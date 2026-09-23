namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One thing the preparation observed about a declaration without refusing the run.
    /// </summary>
    /// <remarks>
    /// Why not a type outcome: two of the worker's diagnostics name no type at all, so a row that
    /// promised a type name per row could not carry them. Why reported anyway: the declaration
    /// stays in the source and keeps not working until a compile, which the reader has to be told.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeNotice
    {
        internal HotReloadIntroducedTypeNotice(
            string ownerProjectRelativePath,
            string text,
            bool namesDeclaration,
            string refusedTypeMetadataName)
        {
            OwnerProjectRelativePath = ownerProjectRelativePath ?? string.Empty;
            Text = text ?? string.Empty;
            NamesDeclaration = namesDeclaration;
            RefusedTypeMetadataName = refusedTypeMetadataName;
        }

        internal string OwnerProjectRelativePath { get; }

        /// <summary>
        /// Whether the notice is about a declaration in its owner file. A diagnostic about the
        /// whole run is attached to every file the worker parsed, so its owner declares nothing
        /// the notice refuses.
        /// </summary>
        internal bool NamesDeclaration { get; }

        internal string Text { get; }

        /// <summary>
        /// The metadata name of the type this notice refused, or null when the notice does not
        /// refuse a type by name. A caller in the same run fails to compile against that type.
        /// </summary>
        internal string RefusedTypeMetadataName { get; }
    }
}
