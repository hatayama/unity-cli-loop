using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates Dynamic Code Compilation Service instances with the dependencies required by this module.
    /// </summary>
    public sealed class DynamicCodeCompilationServiceFactory : IDynamicCompilationServiceFactory
    {
        public IDynamicCompilationService Create()
        {
            return new DynamicCodeCompiler();
        }
    }
}
