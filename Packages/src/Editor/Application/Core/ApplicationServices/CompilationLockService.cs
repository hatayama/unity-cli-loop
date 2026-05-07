using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Port for the compilation lock file used by external CLI processes.
    /// <summary>
    /// Defines the Compilation Lock operations required by the owning workflow.
    /// </summary>
    public interface ICompilationLockService
    {
        void RegisterForEditorStartup();
        void DeleteLockFile();
    }

    // Static facade retained for Unity callbacks and server cleanup paths outside constructor control.
    /// <summary>
    /// Provides Compilation Lock operations for its owning module.
    /// </summary>
    public static class CompilationLockService
    {
        private static ICompilationLockService ServiceValue;

        internal static void RegisterService(ICompilationLockService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static void RegisterForEditorStartup()
        {
            Service.RegisterForEditorStartup();
        }

        public static void DeleteLockFile()
        {
            Service.DeleteLockFile();
        }

        private static ICompilationLockService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop compilation lock service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
