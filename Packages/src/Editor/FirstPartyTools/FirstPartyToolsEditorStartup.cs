using System;

using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps bundled tool initialization inside the bundled-tool assembly.
    /// <summary>
    /// Initializes First Party Tools Editor editor startup behavior.
    /// </summary>
    public static class FirstPartyToolsEditorStartup
    {
        /// <summary>
        /// Initializes every first-party tool's Editor startup work. <paramref name="isToolEnabled"/>
        /// answers whether the user enabled a tool in the tool settings, by its settings name.
        /// </summary>
        public static void Initialize(Func<string, bool> isToolEnabled)
        {
            DomainReloadDisableScopeRecovery.RestoreForEditorStartup();
            // Why every domain load: a Domain Reload can end a run before its finally runs, and a run
            // that survives one is not tracked to its end, so stack traces must not stay off past it.
            RunTestsLogStackTraceSuppressor.CreateDefault().RestoreIfSaved();
            ExternalSceneChangeTracker.Initialize();
            ControlPlayModeEditorStartup.Initialize();
            PausePointEditorStartup.Initialize();
            CompileEditorStartup.Initialize();
            HotReloadEditorStartup.Initialize(isToolEnabled);
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
        /// Starts the first-party tool work that has to begin before this domain answers its first
        /// uloop command: the source snapshot capture, which then runs in the background. Called
        /// once, at the end of the Editor startup. <paramref name="isToolEnabled"/>
        /// answers whether the user enabled a tool in the tool settings, by its settings name.
        /// </summary>
        public static void PrepareBeforeServingCommands(Func<string, bool> isToolEnabled)
        {
            // Why not in an import worker: it never serves commands (the server does not start
            // there) and it shares Library with the main Editor, so a capture there would only
            // race the main Editor's own.
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            HotReloadEditorStartup.StartSourceSnapshotCaptureBeforeServingCommands(isToolEnabled);
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
