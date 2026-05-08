using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    internal static class ToolSettingsUseCaseRegistry
    {
        private static ToolSettingsUseCase RegisteredUseCase;

        internal static void Register(ToolSettingsUseCase useCase)
        {
            Debug.Assert(useCase != null, "useCase must not be null");

            RegisteredUseCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        internal static ToolSettingsUseCase GetRegisteredUseCase()
        {
            if (RegisteredUseCase == null)
            {
                throw new InvalidOperationException("Tool settings use case is not registered.");
            }

            return RegisteredUseCase;
        }
    }
}
