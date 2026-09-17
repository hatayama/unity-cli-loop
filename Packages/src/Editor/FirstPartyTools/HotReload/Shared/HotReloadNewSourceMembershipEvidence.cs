namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Immutable evidence that a source absent from the last compiled source list belongs to an unchanged assembly.
    /// </summary>
    internal sealed class HotReloadNewSourceMembershipEvidence
    {
        internal HotReloadNewSourceMembershipEvidence(
            string projectRelativePath,
            string assemblyName,
            string targetDllPath,
            string targetDllMvid,
            string resolvedAssemblyDefinitionPath,
            HotReloadNewSourceMembershipBoundary[] boundaries)
        {
            ProjectRelativePath = projectRelativePath;
            AssemblyName = assemblyName;
            TargetDllPath = targetDllPath;
            TargetDllMvid = targetDllMvid;
            ResolvedAssemblyDefinitionPath = resolvedAssemblyDefinitionPath;
            Boundaries = boundaries;
        }

        internal string ProjectRelativePath { get; }

        internal string AssemblyName { get; }

        internal string TargetDllPath { get; }

        internal string TargetDllMvid { get; }

        internal string ResolvedAssemblyDefinitionPath { get; }

        internal HotReloadNewSourceMembershipBoundary[] Boundaries { get; }
    }

    /// <summary>
    /// One disk/import assembly boundary captured for a new source membership decision.
    /// </summary>
    internal sealed class HotReloadNewSourceMembershipBoundary
    {
        internal HotReloadNewSourceMembershipBoundary(
            string projectRelativePath,
            string diskContents,
            string importedContents,
            string diskGuid,
            string importedGuid)
        {
            ProjectRelativePath = projectRelativePath;
            DiskContents = diskContents;
            ImportedContents = importedContents;
            DiskGuid = diskGuid;
            ImportedGuid = importedGuid;
        }

        internal string ProjectRelativePath { get; }

        internal string DiskContents { get; }

        internal string ImportedContents { get; }

        internal string DiskGuid { get; }

        internal string ImportedGuid { get; }
    }
}
