using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Stores the third-party tool migration use case for Unity-created presentation objects.
    /// </summary>
    internal static class ThirdPartyToolMigrationUseCaseRegistry
    {
        private static ThirdPartyToolMigrationUseCase RegisteredUseCase;

        internal static void Register(ThirdPartyToolMigrationUseCase useCase)
        {
            Debug.Assert(useCase != null, "useCase must not be null");

            RegisteredUseCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        internal static ThirdPartyToolMigrationUseCase GetRegisteredUseCase()
        {
            if (RegisteredUseCase == null)
            {
                throw new InvalidOperationException("Unity CLI Loop third-party tool migration use case is not registered.");
            }

            return RegisteredUseCase;
        }
    }
}
