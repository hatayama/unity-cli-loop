
using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    internal sealed class PresentationApplicationServices
    {
        internal PresentationApplicationServices(SkillSetupUseCase skillSetupUseCase)
        {
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");

            SkillSetupUseCase = skillSetupUseCase ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
        }

        internal SkillSetupUseCase SkillSetupUseCase { get; }
    }

    // Groups presentation startup behind one facade so UI boot decisions stay in the presentation layer.
    /// <summary>
    /// Initializes Presentation Editor editor startup behavior.
    /// </summary>
    internal static class PresentationEditorStartup
    {
        private static PresentationApplicationServices ServicesValue;

        internal static void RegisterApplicationServices(PresentationApplicationServices services)
        {
            Debug.Assert(services != null, "services must not be null");

            ServicesValue = services ?? throw new ArgumentNullException(nameof(services));
        }

        internal static SkillSetupUseCase SkillSetupUseCase
        {
            get
            {
                if (ServicesValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop presentation application services are not registered.");
                }

                return ServicesValue.SkillSetupUseCase;
            }
        }

        internal static void Initialize()
        {
            SetupWizardWindow.InitializeForEditorStartup();
        }
    }
}
