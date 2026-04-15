using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    public static class DynamicCodeCompilationServiceRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            DynamicCompilationServiceRegistry.RegisterFactory(new DynamicCodeCompilationServiceFactory());
        }
    }
}
