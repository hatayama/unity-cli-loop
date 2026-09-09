using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// What the pause-point tool may ask the hot-reload tool about the domain hot reload
    /// currently has installed. Implemented by hot reload, read through
    /// <see cref="HotReloadPausePointCoordination.HotReloadSide"/>.
    /// </summary>
    public interface IHotReloadPausePointPort
    {
        /// <summary>
        /// Returns the active shim MethodBase for a patched original method, or null when the
        /// method is not hot-reload patched.
        /// </summary>
        MethodBase GetActiveShimForMethod(MethodBase method);

        /// <summary>
        /// Argument is a forward-slash path (absolute or project-relative); returns null when
        /// that file has no active shim generation. A method may still report an active shim via
        /// <see cref="GetActiveShimForMethod"/> while missing from this file lookup (a newer
        /// generation replaced the file and the method was skipped, bind-failed, or
        /// isolation-excluded). Consumers must treat that combination as retarget-impossible
        /// (suppress the marker).
        /// </summary>
        HotReloadShimFileLookup GetShimLookupForFile(string file);

        /// <summary>
        /// Returns the PDB-checksum-verified compiled snapshot text for a project-relative source
        /// file, or null when no snapshot is available.
        /// </summary>
        string GetVerifiedSnapshotSourceForFile(string projectRelativeFile);

        /// <summary>
        /// Returns the PDB-checksum-verified snapshot text for a project-relative source path and
        /// the compiled assembly path, or null when none. Use this after the shim registry is
        /// cleared (revert/restore) when file lookup can no longer find a generation.
        /// </summary>
        string GetVerifiedSnapshotSource(string projectRelativeFile, string dllPath);

        /// <summary>
        /// Returns the LocalBuilder array (shim slot order) from the latest transplant rebuild of
        /// the original method, or null when none. Returned LocalBuilders are tied to the
        /// ILGenerator of that rebuild and are valid only inside the same rebuild (the pause-point
        /// transpiler that runs after the hot-reload transpiler). Do not retain or use them
        /// outside that rebuild.
        /// </summary>
        IReadOnlyList<LocalBuilder> GetTransplantLocals(MethodBase method);

        /// <summary>
        /// Returns how many instructions the latest rebuild prepended before the patched body
        /// (0 when none). Pause-point must add this only to TransplantChainJoin indexes;
        /// ShimDirect and OriginalBody have no transplant preamble.
        /// </summary>
        int GetTransplantPreambleLength(MethodBase method);

        /// <summary>
        /// Argument is a type full name (reflection <c>Outer+Inner</c> or Cecil
        /// <c>Outer/Inner</c>); returns the simple names of fields hot reload added to that type
        /// across every file, or empty when none.
        /// </summary>
        IReadOnlyList<string> GetAddedFieldsForType(string typeFullName);
    }
}
