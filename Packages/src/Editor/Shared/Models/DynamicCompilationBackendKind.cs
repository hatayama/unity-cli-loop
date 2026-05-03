namespace io.github.hatayama.UnityCliLoop
{
    public enum DynamicCompilationBackendKind
    {
        Unknown = 0,
        SharedRoslynWorker = 1,
        OneShotRoslyn = 2,
        AssemblyBuilderFallback = 3
    }
}
