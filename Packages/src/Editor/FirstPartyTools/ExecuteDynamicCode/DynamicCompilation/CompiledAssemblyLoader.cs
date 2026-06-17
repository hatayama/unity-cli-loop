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
        public static CompiledAssemblyLoadResult Load(byte[] assemblyBytes)
        {
            Debug.Assert(assemblyBytes != null, "assemblyBytes must not be null");

            Stopwatch stopwatch = Stopwatch.StartNew();
            Assembly compiledAssembly = Assembly.Load(assemblyBytes);

            stopwatch.Stop();
            return new CompiledAssemblyLoadResult(
                true,
                compiledAssembly,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
