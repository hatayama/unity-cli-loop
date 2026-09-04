using System;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain seam so HotReload can resolve external scene changes before
    /// Refresh without referencing the Compile assembly.
    /// </summary>
    public static class ExternalSceneChangeCoordination
    {
        /// <summary>
        /// Set by CompileEditorStartup. Argument is reloadExternalSceneChanges;
        /// returns whether Refresh may proceed.
        /// </summary>
        public static Func<bool, (bool CanProceed, string Message, string[] ScenePaths)>
            ResolveBeforeRefresh { get; set; }
    }
}
