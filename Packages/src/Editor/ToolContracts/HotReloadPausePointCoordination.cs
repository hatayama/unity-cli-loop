using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point between the hot-reload tool and the source
    /// pause-point tool. The two tools live in sibling assemblies that must not
    /// reference each other, so each side publishes its state through delegates
    /// wired in its own static constructor (the same pattern as
    /// UloopPausePointRegistry's OnCleared wiring). A null delegate means the owning
    /// side has not initialized in this domain, which also means it has no state
    /// worth querying; callers treat null as "no patches" / "no markers" /
    /// "no shim lookup".
    /// </summary>
    public static class HotReloadPausePointCoordination
    {
        // Set by HotReloadPatcher. Returns the active shim MethodBase for a patched
        // original method, or null when the method is not hot-reload patched.
        public static Func<MethodBase, MethodBase> GetActiveShimForMethod { get; set; }

        // Set by HotReloadPatcher. Returns how many methods are currently hot-reload
        // patched (0 when none). Null means the hot-reload tool has not initialized in
        // this domain, which also means no patches exist - callers treat null as 0.
        public static Func<int> GetActiveHotReloadPatchCount { get; set; }

        /// <summary>
        /// Set by HotReloadShimRegistry. Argument is a forward-slash path (absolute or
        /// project-relative); returns null when that file has no active shim generation.
        /// A method may still report an active shim via <see cref="GetActiveShimForMethod"/>
        /// while missing from this file lookup (a newer generation replaced the file and
        /// the method was skipped, bind-failed, or isolation-excluded). Consumers must treat
        /// that combination as retarget-impossible (suppress the marker).
        /// </summary>
        public static Func<string, HotReloadShimFileLookup> GetShimLookupForFile { get; set; }

        /// <summary>
        /// Set by HotReload. Returns the PDB-checksum-verified compiled snapshot text for a
        /// project-relative source file, or null when no snapshot is available.
        /// </summary>
        public static Func<string, string> GetVerifiedSnapshotSourceForFile { get; set; }

        /// <summary>
        /// Set by HotReloadPatcher. Returns the LocalBuilder array (shim slot order) from
        /// the latest transplant rebuild of the original method, or null when none.
        /// Returned LocalBuilders are tied to the ILGenerator of that rebuild and are valid
        /// only inside the same rebuild (the pause-point transpiler that runs after the
        /// hot-reload transpiler). Do not retain or use them outside that rebuild.
        /// </summary>
        public static Func<MethodBase, IReadOnlyList<LocalBuilder>> GetTransplantLocals { get; set; }

        /// <summary>
        /// Set by HotReloadPatcher. Returns how many instructions the latest rebuild prepended
        /// before the patched body (0 when none). Pause-point must add this only to
        /// TransplantChainJoin indexes; ShimDirect and OriginalBody have no transplant preamble.
        /// </summary>
        public static Func<MethodBase, int> GetTransplantPreambleLength { get; set; }

        // Set by SourcePausePointPatcher. Returns the marker ids currently injected
        // into the method (empty when none).
        public static Func<MethodBase, IReadOnlyList<string>> GetArmedMarkerIdsOnMethod { get; set; }

        // Set by SourcePausePointPatcher. Returns marker ids whose logical owner is the
        // method and whose registry entry is currently SuppressedByHotReload.
        public static Func<MethodBase, IReadOnlyList<string>> GetSuppressedMarkerIdsOnMethod { get; set; }

        // Set by SourcePausePointPatcher. Drains marker ids recorded during the latest
        // hot-reload patch transition that were skipped for retarget because they were
        // already Expired (not a scan of residual expired ledger state).
        public static Func<IReadOnlyList<string>> ConsumeExpiredNotRetargetedMarkerIds { get; set; }

        // Set by SourcePausePointPatcher. Drains (id, oldText, newText) triples recorded when
        // retarget changed the resolved line text of an armed marker.
        public static Func<IReadOnlyList<(string Id, string OldText, string NewText)>>
            ConsumeRetargetLineDriftWarnings { get; set; }

        // Set by SourcePausePointPatcher. Invoked by HotReloadPatcher after a
        // method's patch state changes (true = patched, false = reverted).
        public static Action<MethodBase, bool> OnHotReloadPatchStateChanged { get; set; }
    }

    /// <summary>
    /// One method registered in an active hot-reload shim generation for a file.
    /// BCL types only so PausePoint can consume it without referencing HotReload.
    /// </summary>
    public sealed class HotReloadShimMethodLookup
    {
        public MethodBase OriginalMethod { get; }
        public MethodBase ShimMethod { get; }
        public bool IsDelegation { get; }
        public int SourceStartLine { get; }
        public int SourceEndLine { get; }

        public HotReloadShimMethodLookup(
            MethodBase originalMethod,
            MethodBase shimMethod,
            bool isDelegation,
            int sourceStartLine,
            int sourceEndLine)
        {
            OriginalMethod = originalMethod;
            ShimMethod = shimMethod;
            IsDelegation = isDelegation;
            SourceStartLine = sourceStartLine;
            SourceEndLine = sourceEndLine;
        }
    }

    /// <summary>
    /// Active shim assembly bytes and per-method lookups for one edited source file.
    /// </summary>
    public sealed class HotReloadShimFileLookup
    {
        public byte[] AssemblyBytes { get; }
        public byte[] PdbBytes { get; }
        public Assembly LoadedAssembly { get; }
        public IReadOnlyList<HotReloadShimMethodLookup> Methods { get; }

        public HotReloadShimFileLookup(
            byte[] assemblyBytes,
            byte[] pdbBytes,
            Assembly loadedAssembly,
            IReadOnlyList<HotReloadShimMethodLookup> methods)
        {
            AssemblyBytes = assemblyBytes;
            PdbBytes = pdbBytes;
            LoadedAssembly = loadedAssembly;
            Methods = methods;
        }
    }
}
