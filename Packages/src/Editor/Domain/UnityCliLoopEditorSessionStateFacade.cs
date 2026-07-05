using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Exposes the CompositionRoot-owned UnityCliLoopEditorSessionStateService to call sites
    /// that cannot receive it through constructor injection.
    /// Why: static bridge command handlers (dispatched by IPC routers) and Activator.CreateInstance
    /// -built tools have no injection seam, so they need to read the shared service without
    /// rebuilding an ad hoc instance that would diverge from the CompositionRoot wiring.
    /// </summary>
    public static class UnityCliLoopEditorSessionStateFacade
    {
        private static UnityCliLoopEditorSessionStateService ServiceValue;

        internal static void RegisterService(UnityCliLoopEditorSessionStateService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static UnityCliLoopEditorSessionStateService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException(
                        "Unity CLI Loop editor session state service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
