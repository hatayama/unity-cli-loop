namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What one run did with one type declaration, reported per type instead of per method.
    /// </summary>
    /// <remarks>
    /// Why its own row and not a method outcome: a type is not a patched body, so counting it in
    /// the patch totals or listing it among the methods would report a reload that changed no
    /// method body as one that patched something.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeOutcome
    {
        private HotReloadIntroducedTypeOutcome(
            HotReloadIntroducedTypeOutcomeKind kind,
            string metadataName,
            string originalAssemblyName,
            string ownerProjectRelativePath,
            string reason)
        {
            Kind = kind;
            MetadataName = metadataName ?? string.Empty;
            OriginalAssemblyName = originalAssemblyName ?? string.Empty;
            OwnerProjectRelativePath = ownerProjectRelativePath ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public HotReloadIntroducedTypeOutcomeKind Kind { get; }

        public string MetadataName { get; }

        public string OriginalAssemblyName { get; }

        /// <summary>The file that declares the type, or empty when the run cannot attribute it.</summary>
        public string OwnerProjectRelativePath { get; }

        public string Reason { get; }

        /// <summary>A type this run compiled into an artifact and activated at the commit boundary.</summary>
        public static HotReloadIntroducedTypeOutcome Introduced(
            string metadataName,
            string originalAssemblyName,
            string ownerProjectRelativePath)
        {
            return new HotReloadIntroducedTypeOutcome(
                HotReloadIntroducedTypeOutcomeKind.Introduced,
                metadataName,
                originalAssemblyName,
                ownerProjectRelativePath,
                string.Empty);
        }

        /// <summary>
        /// A declaration this run bound from an artifact the domain already retains, which is why
        /// the run introduced nothing for it.
        /// </summary>
        public static HotReloadIntroducedTypeOutcome AlreadyActive(
            string metadataName,
            string originalAssemblyName,
            string ownerProjectRelativePath)
        {
            return new HotReloadIntroducedTypeOutcome(
                HotReloadIntroducedTypeOutcomeKind.AlreadyActive,
                metadataName,
                originalAssemblyName,
                ownerProjectRelativePath,
                HotReloadConstants.AlreadyActiveIntroducedTypeReason);
        }

        /// <summary>
        /// A declaration this run refused: it redefines a type the domain retains, two files of
        /// the group declare it, or its artifact did not compile. A run-level failure of the
        /// preparation itself is not one of these, because it refused no declaration.
        /// </summary>
        public static HotReloadIntroducedTypeOutcome Failed(
            string metadataName,
            string originalAssemblyName,
            string ownerProjectRelativePath,
            string reason)
        {
            return new HotReloadIntroducedTypeOutcome(
                HotReloadIntroducedTypeOutcomeKind.Failed,
                metadataName,
                originalAssemblyName,
                ownerProjectRelativePath,
                reason);
        }
    }

    internal enum HotReloadIntroducedTypeOutcomeKind
    {
        Introduced = 0,
        AlreadyActive = 1,
        Failed = 2
    }
}
