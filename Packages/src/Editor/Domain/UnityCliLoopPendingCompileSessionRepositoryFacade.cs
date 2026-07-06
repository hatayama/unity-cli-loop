using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Exposes the CompositionRoot-owned pending-compile repository to static call sites.
    /// Why: Activator-created tools and static bridge handlers need the shared repository without
    /// reconstructing infrastructure outside the CompositionRoot.
    /// </summary>
    public static class UnityCliLoopPendingCompileSessionRepositoryFacade
    {
        private static IPendingCompileSessionRepository RepositoryValue;

        internal static void RegisterRepository(IPendingCompileSessionRepository repository)
        {
            Debug.Assert(repository != null, "repository must not be null");

            RepositoryValue = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public static IPendingCompileSessionRepository Repository
        {
            get
            {
                if (RepositoryValue == null)
                {
                    throw new InvalidOperationException(
                        "Unity CLI Loop pending-compile session repository is not registered.");
                }

                return RepositoryValue;
            }
        }
    }
}
