using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Defines the Compilation Readiness operations required by the owning workflow.
    /// </summary>
    public interface ICompilationReadinessService
    {
        void RegisterForEditorStartup();
    }

    // Static facade retained for Unity callbacks and server cleanup paths outside constructor control.
    /// <summary>
    /// Provides Compilation Readiness operations for its owning module.
    /// </summary>
    public static class CompilationReadinessService
    {
        private static ICompilationReadinessService ServiceValue;

        internal static void RegisterService(ICompilationReadinessService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static void RegisterForEditorStartup()
        {
            Service.RegisterForEditorStartup();
        }

        private static ICompilationReadinessService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop compilation readiness service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
