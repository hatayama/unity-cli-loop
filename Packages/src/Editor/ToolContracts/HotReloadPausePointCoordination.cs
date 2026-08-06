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

        // Set by HotReloadShimRegistry. Argument is a forward-slash path (absolute or
        // project-relative); returns null when that file has no active shim generation.
        public static Func<string, HotReloadShimFileLookup> GetShimLookupForFile { get; set; }

        // Set by HotReloadPatcher. Returns the LocalBuilder array (shim slot order) from
        // the latest transplant rebuild of the original method, or null when none.
        public static Func<MethodBase, IReadOnlyList<LocalBuilder>> GetTransplantLocals { get; set; }

        // Set by SourcePausePointPatcher. Returns the marker ids currently injected
        // into the method (empty when none).
        public static Func<MethodBase, IReadOnlyList<string>> GetArmedMarkerIdsOnMethod { get; set; }

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
