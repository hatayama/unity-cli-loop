
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides External Compiler Path Resolution operations for its owning module.
    /// </summary>
    internal sealed class ExternalCompilerPathResolutionService
    {
        public ExternalCompilerPaths Resolve()
        {
            return ExternalCompilerPathResolver.Resolve();
        }
    }
}
