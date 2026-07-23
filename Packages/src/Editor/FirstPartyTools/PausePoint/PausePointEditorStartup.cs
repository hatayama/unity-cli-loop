namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps pause-point startup wiring inside the pause-point assembly so the composition
    // root only depends on the bundled-tool facade.
    internal static class PausePointEditorStartup
    {
        public static void Initialize()
        {
            PausePointDomainReloadTracker.MarkDomainLoaded();
        }
    }
}
