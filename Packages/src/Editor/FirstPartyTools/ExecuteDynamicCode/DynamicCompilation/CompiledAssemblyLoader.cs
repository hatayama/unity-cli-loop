using System.Diagnostics;
using Assembly = System.Reflection.Assembly;
using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Compiled Assembly Loader behavior for Unity CLI Loop.
    /// </summary>
    internal static class CompiledAssemblyLoader
    {
        public static CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes)
        {
            Debug.Assert(assemblyBytes != null, "assemblyBytes must not be null");

            Stopwatch stopwatch = Stopwatch.StartNew();
            Assembly compiledAssembly = pdbBytes != null && pdbBytes.Length > 0
                ? Assembly.Load(assemblyBytes, pdbBytes)
                : Assembly.Load(assemblyBytes);

            stopwatch.Stop();
            return new CompiledAssemblyLoadResult(
                true,
                compiledAssembly,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
