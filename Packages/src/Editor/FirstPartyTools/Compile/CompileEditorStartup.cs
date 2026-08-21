namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps compile startup wiring inside the compile assembly so the composition
    // root only depends on the bundled-tool facade.
    internal static class CompileEditorStartup
    {
        public static void Initialize()
        {
            CompileApiUpdaterConsentPatcher.Install();
        }
    }
}
