
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Exposes the dynamic-code tool startup sequence without exposing implementation collaborators publicly.
    /// <summary>
    /// Initializes Execute Dynamic Code Editor editor startup behavior.
    /// </summary>
    internal static class ExecuteDynamicCodeEditorStartup
    {
        internal static void Initialize()
        {
            AssemblyTypeIndex.InvalidateForEditorStartup();
            DynamicReferenceSetBuilder.InvalidateReferenceCacheForEditorStartup();
            SharedRoslynCompilerWorkerHost.RegisterLifecycleForEditorStartup();
            DynamicCodeServices.RequestStartupPrewarm();
        }

        internal static void ResetServerScopedServices()
        {
            DynamicCodeServices.ResetServerScopedServices();
            DynamicCodeServices.RequestStartupPrewarm();
        }
    }
}
