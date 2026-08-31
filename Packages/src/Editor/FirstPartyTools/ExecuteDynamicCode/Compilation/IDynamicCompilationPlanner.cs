
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the dynamic code source preparation operations required by the owning workflow.
    /// </summary>
    public interface IDynamicCodeSourcePreparationService
    {
        PreparedDynamicCode Prepare(
            string source,
            string namespaceName,
            string className);
    }

    /// <summary>
    /// Defines planning operations for dynamic compilation behavior.
    /// </summary>
    public interface IDynamicCompilationPlanner
    {
        DynamicCompilationPlan CreatePlan(CompilationRequest request);
    }
}
