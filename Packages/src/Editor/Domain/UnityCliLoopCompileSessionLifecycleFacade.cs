using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Exposes the CompositionRoot-owned compile session lifecycle service to static call sites.
    /// Why: static bridge command handlers and Activator-created tools cannot receive constructor
    /// injection, so they read the shared CompositionRoot instance through this narrow facade.
    /// </summary>
    public static class UnityCliLoopCompileSessionLifecycleFacade
    {
        private static UnityCliLoopCompileSessionLifecycleService ServiceValue;

        internal static void RegisterService(UnityCliLoopCompileSessionLifecycleService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static UnityCliLoopCompileSessionLifecycleService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException(
                        "Unity CLI Loop compile session lifecycle service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
