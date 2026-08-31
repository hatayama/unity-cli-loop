using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Exposes the CompositionRoot-owned compile-result repository to static call sites.
    /// Why: Activator-created tools and static bridge handlers need the shared repository without
    /// reconstructing infrastructure outside the CompositionRoot.
    /// </summary>
    public static class UnityCliLoopCompileResultSessionRepositoryFacade
    {
        private static ICompileResultSessionRepository RepositoryValue;

        internal static void RegisterRepository(ICompileResultSessionRepository repository)
        {
            Debug.Assert(repository != null, "repository must not be null");

            RepositoryValue = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public static ICompileResultSessionRepository Repository
        {
            get
            {
                if (RepositoryValue == null)
                {
                    throw new InvalidOperationException(
                        "Unity CLI Loop compile-result session repository is not registered.");
                }

                return RepositoryValue;
            }
        }
    }
}
