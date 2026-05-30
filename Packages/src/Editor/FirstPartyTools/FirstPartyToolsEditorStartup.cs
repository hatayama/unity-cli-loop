namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps bundled tool initialization inside the bundled-tool assembly.
    /// <summary>
    /// Initializes First Party Tools Editor editor startup behavior.
    /// </summary>
    public static class FirstPartyToolsEditorStartup
    {
        public static void Initialize()
        {
            CompileDomainReloadRecoveryStartup.Initialize();
            ExecuteDynamicCodeEditorStartup.Initialize();
            GetLogsEditorStartup.Initialize();
#if ULOOP_HAS_TEST_FRAMEWORK
            RunTestsTestFrameworkStartup.Initialize();
#endif
#if ULOOP_HAS_INPUT_SYSTEM
            RecordInputEditorStartup.Initialize();
            ReplayInputEditorStartup.Initialize();
            SimulateKeyboardEditorStartup.Initialize();
            SimulateMouseInputEditorStartup.Initialize();
            SimulateMouseUiEditorStartup.Initialize();
#endif
        }

        public static void ResetServerScopedServices()
        {
            ExecuteDynamicCodeEditorStartup.ResetServerScopedServices();
        }

        public static void ResetServerScopedServicesBeforeDomainReload()
        {
            ExecuteDynamicCodeEditorStartup.ResetServerScopedServicesBeforeDomainReload();
        }

        public static string CreateExecuteDynamicCodeReadinessProbeCode()
        {
            // Why: composition root can only depend on the bundled-tool facade assembly,
            // so the dynamic-code assembly keeps ownership of the actual probe source shape.
            return ExecuteDynamicCodeReadinessProbe.CreatePrimaryReturnStringProbeCode();
        }
    }
}
