namespace io.github.hatayama.uLoopMCP
{
    internal sealed class ExternalCompilerPathResolutionService
    {
        public ExternalCompilerPaths Resolve()
        {
            return ExternalCompilerPathResolver.Resolve();
        }
    }
}
