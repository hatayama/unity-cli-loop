namespace io.github.hatayama.UnityCliLoop
{
    internal interface IDynamicCompilationPlanner
    {
        DynamicCompilationPlan CreatePlan(CompilationRequest request);
    }
}
