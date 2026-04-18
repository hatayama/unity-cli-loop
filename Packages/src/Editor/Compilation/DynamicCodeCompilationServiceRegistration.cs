using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    public static class DynamicCodeCompilationServiceRegistration
    {
        private static readonly object SyncRoot = new object();

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EnsureRegistered();
        }

        internal static void EnsureRegistered()
        {
            lock (SyncRoot)
            {
                if (DynamicCompilationServiceRegistry.HasRegisteredFactory)
                {
                    return;
                }

                DynamicCompilationServiceRegistry.RegisterFactory(new DynamicCodeCompilationServiceFactory());
            }
        }
    }
}
