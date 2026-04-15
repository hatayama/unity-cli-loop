namespace io.github.hatayama.uLoopMCP
{
    internal interface IDynamicCompilationPlanner
    {
        DynamicCompilationPlan CreatePlan(CompilationRequest request);
    }
}
