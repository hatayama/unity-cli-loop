namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Holds file-service constants shared by migration scanners and planners.
    /// </summary>
    internal static class ThirdPartyToolMigrationFileServiceConstants
    {
        internal const int WindowsLegacyMaxPathLength = 260;
        internal const string WindowsExtendedLengthPathPrefix = @"\\?\";
        internal const string WindowsExtendedLengthUncPathPrefix = @"\\?\UNC\";
        internal const string WindowsUncPathPrefix = @"\\";
        internal const string ImplicitEditorAssemblyDirectoryName = "__UnityCliLoopImplicitEditorAssembly";
        internal const string ImplicitRuntimeAssemblyDirectoryName = "__UnityCliLoopImplicitRuntimeAssembly";
        internal const string ImplicitFirstPassEditorAssemblyDirectoryName =
            "__UnityCliLoopImplicitFirstPassEditorAssembly";
        internal const string ImplicitFirstPassRuntimeAssemblyDirectoryName =
            "__UnityCliLoopImplicitFirstPassRuntimeAssembly";
        internal const int PreviewYieldBatchSize = 32;
    }
}
