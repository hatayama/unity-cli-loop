using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the result data produced by Compiled Assembly Load behavior.
    /// </summary>
    public sealed class CompiledAssemblyLoadResult
    {
        public bool Success { get; }

        public Assembly CompiledAssembly { get; }

        public double AssemblyLoadMilliseconds { get; }

        public CompiledAssemblyLoadResult(
            bool success,
            Assembly compiledAssembly,
            double assemblyLoadMilliseconds)
        {
            Success = success;
            CompiledAssembly = compiledAssembly;
            AssemblyLoadMilliseconds = assemblyLoadMilliseconds;
        }
    }
}
