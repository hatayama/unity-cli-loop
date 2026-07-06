using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Exposes the CompositionRoot-owned session flags repository to static entrypoints.
    /// </summary>
    public static class UnityCliLoopSessionFlagsFacade
    {
        private static ISessionFlagsRepository RepositoryValue;

        internal static void RegisterRepository(ISessionFlagsRepository repository)
        {
            Debug.Assert(repository != null, "repository must not be null");

            RepositoryValue = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public static ISessionFlagsRepository Repository
        {
            get
            {
                if (RepositoryValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop session flags repository is not registered.");
                }

                return RepositoryValue;
            }
        }
    }
}
