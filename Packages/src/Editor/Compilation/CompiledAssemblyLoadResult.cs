using System.Collections.Generic;
using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class CompiledAssemblyLoadResult
    {
        public bool Success { get; }

        public Assembly CompiledAssembly { get; }

        public List<SecurityViolation> SecurityViolations { get; }

        public double AssemblyLoadMilliseconds { get; }

        public CompiledAssemblyLoadResult(
            bool success,
            Assembly compiledAssembly,
            List<SecurityViolation> securityViolations,
            double assemblyLoadMilliseconds)
        {
            Success = success;
            CompiledAssembly = compiledAssembly;
            SecurityViolations = securityViolations;
            AssemblyLoadMilliseconds = assemblyLoadMilliseconds;
        }
    }
}
