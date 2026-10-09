using UnityEditor;

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
            DomainReloadDisableScopeRecovery.RestoreForEditorStartup();
            ExternalSceneChangeTracker.Initialize();
            ControlPlayModeEditorStartup.Initialize();
            PausePointEditorStartup.Initialize();
            CompileEditorStartup.Initialize();
            HotReloadEditorStartup.Initialize();
            WatchEditorStartup.Initialize();
            ExecuteDynamicCodeEditorStartup.Initialize();
            GetLogsEditorStartup.Initialize();
            ScreenshotEditorStartup.Initialize();
            RecordVideoEditorStartup.Initialize();
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

        /// <summary>
        /// Runs the first-party tool work that has to finish before this domain answers its first
        /// uloop command. Called once, at the end of the Editor startup.
        /// </summary>
        public static void PrepareBeforeServingCommands()
        {
            // Why not in an import worker: it never serves commands (the server does not start
            // there) and it shares Library with the main Editor, so a capture there would only
            // race the main Editor's own.
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            HotReloadEditorStartup.CaptureSourceSnapshotBeforeServingCommands();
        }

        public static void ResetServerScopedServices()
        {
            ExecuteDynamicCodeEditorStartup.ResetServerScopedServices();
        }

        public static void ResetServerScopedServicesBeforeDomainReload()
        {
            ExecuteDynamicCodeEditorStartup.ResetServerScopedServicesBeforeDomainReload();
        }
    }
}
