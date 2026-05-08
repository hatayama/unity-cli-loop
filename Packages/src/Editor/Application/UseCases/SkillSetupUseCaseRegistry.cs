using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Stores the skill setup use case for Unity-created presentation objects.
    /// </summary>
    internal static class SkillSetupUseCaseRegistry
    {
        private static SkillSetupUseCase RegisteredUseCase;

        internal static void Register(SkillSetupUseCase useCase)
        {
            Debug.Assert(useCase != null, "useCase must not be null");

            RegisteredUseCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        internal static SkillSetupUseCase GetRegisteredUseCase()
        {
            if (RegisteredUseCase == null)
            {
                throw new InvalidOperationException("Unity CLI Loop skill setup use case is not registered.");
            }

            return RegisteredUseCase;
        }
    }
}
