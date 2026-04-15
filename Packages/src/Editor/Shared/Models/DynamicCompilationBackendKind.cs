namespace io.github.hatayama.uLoopMCP
{
    public enum DynamicCompilationBackendKind
    {
        Unknown = 0,
        SharedRoslynWorker = 1,
        OneShotRoslyn = 2,
        AssemblyBuilderFallback = 3
    }
}
