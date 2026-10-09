using System.IO;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps hot-reload startup wiring inside the hot-reload assembly so the composition
    // root only depends on the bundled-tool facade.
    internal static class HotReloadEditorStartup
    {
        public static void Initialize()
        {
            // Why not EditorApplication.delayCall: a cold-start session that hits Unity's native
            // "Scripts have compiler errors" dialog never flushes delayCall again for the rest of
            // that process's lifetime, even for later registrations — while
            // EditorApplication.update keeps ticking (see SetupWizardWindow.cs:56-70). The
            // hot-reload apply entry makes sure of the capture through the same gate before it
            // reads a snapshot; this tick still captures as early as it can when no request comes
            // first, because a file edited before the capture is snapshotted with that edit.
            void CaptureOnFirstUpdateTick()
            {
                EditorApplication.update -= CaptureOnFirstUpdateTick;
                HotReloadCompositionRoot.Services.SourceSnapshotCapture.EnsureCaptured(HotReloadConstants.SourceSnapshotCaptureTriggerFirstUpdateTick);
            }

            // Why a callback of its own rather than a line in the capture above: an exception in
            // one of the two must not keep the other from running. Why unsubscribe first: a sweep
            // that throws is then not retried on every later tick.
            void SweepArtifactsOnFirstUpdateTick()
            {
                EditorApplication.update -= SweepArtifactsOnFirstUpdateTick;
                HotReloadIntroducedTypePreparation.SweepArtifactsOfEarlierDomains(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            }

            // Why after the capture and the sweep: the first run of this domain reads the snapshot
            // the capture writes, and the warm-up only reads what a run would read; neither must
            // wait for the other to be scheduled.
            void StartWarmUpOnFirstUpdateTick()
            {
                EditorApplication.update -= StartWarmUpOnFirstUpdateTick;
                HotReloadCompositionRoot.Services.WarmUp.Start();
            }

            // The services are rebuilt here rather than on first use because the introduced type
            // resolver subscribes to AppDomain.AssemblyResolve when it is built, and that
            // subscription is lost on every domain reload.
            HotReloadCompositionRoot.Initialize();
            // Why right after the services exist: a reload after Play entry has to see the
            // companions the reloads before it were given, and nothing else can name them.
            HotReloadCompanionSourceSessionStore.Load(HotReloadCompositionRoot.Services.Domain.CompanionSources);
            // Why here and not in a static constructor of the counting side: a static constructor
            // runs when something first touches that type, which a domain that only introduced a
            // type may never do, and the tools that warn about a domain reload would then read a
            // null delegate as "nothing to lose".
            HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount =
                () => HotReloadCompositionRoot.Services.Domain.CountActiveChanges().RuntimeChangeTotal;
            // Why the same shape for these two: both run from Editor callbacks that take no
            // argument, so they have to read whichever services are installed when they fire.
            HotReloadAutoRefreshHold.GetServices = () => HotReloadCompositionRoot.Services;
            HotReloadPlayModeEntryDropRecorder.GetServices = () => HotReloadCompositionRoot.Services;
            HotReloadWarmUpEditorHooks.GetServices = () => HotReloadCompositionRoot.Services;
            HotReloadUnityMessageForwardingEditorHooks.GetForwarding =
                () => HotReloadCompositionRoot.Services.UnityMessageForwarding;
            HotReloadWiredValueEditorHooks.GetPersistence =
                () => HotReloadCompositionRoot.Services.WiredValuePersistence;
            EditorApplication.update += CaptureOnFirstUpdateTick;
            EditorApplication.update += SweepArtifactsOnFirstUpdateTick;
            EditorApplication.update += StartWarmUpOnFirstUpdateTick;
            HotReloadPlayModeEntryDropRecorder.Initialize();
            HotReloadAutoRefreshHold.Initialize();
            HotReloadWarmUpEditorHooks.Initialize();
            HotReloadUnityMessageForwardingEditorHooks.Initialize();
            HotReloadWiredValueEditorHooks.Initialize();
            TransformWorkerHostLifecycle.RegisterForEditorStartup();
        }

        internal static void CaptureSourceSnapshotBeforeServingCommands()
        {
        }
    }
}
