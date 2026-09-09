using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point between the hot-reload tool and the source
    /// pause-point tool. The two tools live in sibling assemblies that must not
    /// reference each other, so each side publishes one port implementation, wired by the
    /// side that owns it (the same pattern as UloopPausePointRegistry's OnCleared wiring).
    /// A null port means the owning side has not initialized in this domain, which also
    /// means it has no state worth querying; callers treat null as "no patches" /
    /// "no markers" / "no shim lookup".
    /// </summary>
    public static class HotReloadPausePointCoordination
    {
        /// <summary>
        /// Set by the hot-reload composition root when it installs a domain, and cleared when it
        /// uninstalls one. Never points at a domain that is no longer installed.
        /// </summary>
        public static IHotReloadPausePointPort HotReloadSide { get; set; }

        /// <summary>
        /// Set by the pause-point patcher when it initializes in this domain.
        /// </summary>
        public static IPausePointHotReloadPort PausePointSide { get; set; }
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
