namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class ExternalCompilerPathResolutionService
    {
        public ExternalCompilerPaths Resolve()
        {
            return ExternalCompilerPathResolver.Resolve();
        }
    }
}
