
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Code Source Preparation operations for its owning module.
    /// </summary>
    internal sealed class DynamicCodeSourcePreparationService : IDynamicCodeSourcePreparationService
    {
        public PreparedDynamicCode Prepare(
            string source,
            string namespaceName,
            string className)
        {
            return DynamicCodeSourcePreparer.Prepare(source, namespaceName, className);
        }
    }
}
