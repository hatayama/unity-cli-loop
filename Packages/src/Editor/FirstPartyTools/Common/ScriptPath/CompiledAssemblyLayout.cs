using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides where the compiled assemblies of a project root live.
    /// </summary>
    internal sealed class CompiledAssemblyLayout
    {
        internal static CompiledAssemblyLayout Resolve(string projectRoot)
        {
            throw new NotImplementedException();
        }

        internal string ProjectRoot => throw new NotImplementedException();

        internal bool IsVirtualPlayer => throw new NotImplementedException();

        internal string MainProjectRoot => throw new NotImplementedException();

        internal string CompiledAssembliesDirectory => throw new NotImplementedException();

        internal string DllPath(string assemblyName)
        {
            throw new NotImplementedException();
        }

        internal string PdbPath(string assemblyName)
        {
            throw new NotImplementedException();
        }
    }
}
