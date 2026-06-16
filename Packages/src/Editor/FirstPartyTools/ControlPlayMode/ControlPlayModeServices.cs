namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns Control Play Mode services for one Editor domain.
    /// </summary>
    internal sealed class ControlPlayModeServicesRegistry
    {
        internal ControlPlayModeCompilationFailureService CompilationFailureService { get; } = new();
        internal ControlPlayModeCompilationFailureGate CompilationFailureGate { get; } = new();

        internal void InitializeForEditorStartup()
        {
            CompilationFailureService.InitializeForEditorStartup();
        }
    }

    /// <summary>
    /// Exposes the Control Play Mode service registry to parameterless tool construction.
    /// </summary>
    internal static class ControlPlayModeServices
    {
        private static readonly ControlPlayModeServicesRegistry RegistryValue = new();

        internal static IControlPlayModeCompilationFailureProvider CompilationFailureProvider =>
            RegistryValue.CompilationFailureService;

        internal static IControlPlayModeCompilationFailureGate CompilationFailureGate =>
            RegistryValue.CompilationFailureGate;

        internal static void InitializeForEditorStartup()
        {
            RegistryValue.InitializeForEditorStartup();
        }
    }
}
