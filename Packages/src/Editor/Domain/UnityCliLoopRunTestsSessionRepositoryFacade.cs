using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Exposes the CompositionRoot-owned run-tests session repository to static call sites.
    /// Why: Activator-created tools and static bridge handlers need the shared repository without
    /// reconstructing infrastructure outside the CompositionRoot.
    /// </summary>
    public static class UnityCliLoopRunTestsSessionRepositoryFacade
    {
        private static IRunTestsSessionRepository RepositoryValue;

        internal static void RegisterRepository(IRunTestsSessionRepository repository)
        {
            Debug.Assert(repository != null, "repository must not be null");

            RepositoryValue = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public static IRunTestsSessionRepository Repository
        {
            get
            {
                if (RepositoryValue == null)
                {
                    throw new InvalidOperationException(
                        "Unity CLI Loop run-tests session repository is not registered.");
                }

                return RepositoryValue;
            }
        }
    }
}
