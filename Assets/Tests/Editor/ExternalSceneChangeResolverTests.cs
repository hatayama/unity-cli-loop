using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests external Scene change resolution without invoking Unity's real Scene loading APIs.
    /// </summary>
    public sealed class ExternalSceneChangeResolverTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private static readonly DateTime SavedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime ChangedTime = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);

        [Test]
        public void ResolveExternalSceneChanges_WhenCleanSceneChangedAndReloadEnabled_ReloadsSceneSetup()
        {
            // Verifies default compile behavior reloads clean externally changed Scenes.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: false) },
                _ => (true, ChangedTime, 20),
                () => Array.Empty<string>(),
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(reloadWasCalled, Is.True);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenDirtySceneChangedAndReloadEnabled_ReturnsFailureWithoutMutation()
        {
            // Verifies dirty externally changed Scenes are not overwritten by automatic compile preflight.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveDirtyOpenScenesWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, ChangedTime, 20),
                () =>
                {
                    saveDirtyOpenScenesWasCalled = true;
                    return Array.Empty<string>();
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.False);
            Assert.That(result.Message, Does.Contain("not overwritten automatically"));
            Assert.That(result.ScenePaths, Is.EqualTo(new[] { ScenePath }));
            Assert.That(saveDirtyOpenScenesWasCalled, Is.False);
            Assert.That(reloadWasCalled, Is.False);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenStopPolicyIsEnabled_ReturnsFailureWithoutMutation()
        {
            // Verifies stop-on-external-scene-changes prevents automatic save or reload.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveDirtyOpenScenesWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, ChangedTime, 20),
                () =>
                {
                    saveDirtyOpenScenesWasCalled = true;
                    return Array.Empty<string>();
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: false);

            Assert.That(result.CanProceed, Is.False);
            Assert.That(result.Message, Does.Contain("--stop-on-external-scene-changes"));
            Assert.That(result.ScenePaths, Is.EqualTo(new[] { ScenePath }));
            Assert.That(saveDirtyOpenScenesWasCalled, Is.False);
            Assert.That(reloadWasCalled, Is.False);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenCleanSceneChangedAndReloadEnabled_SavesDirtyScenesBeforeReload()
        {
            // Verifies reload preflight saves other dirty open Scenes before restoring Scene setup.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveDirtyOpenScenesWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: false) },
                _ => (true, ChangedTime, 20),
                () =>
                {
                    saveDirtyOpenScenesWasCalled = true;
                    return Array.Empty<string>();
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(saveDirtyOpenScenesWasCalled, Is.True);
            Assert.That(reloadWasCalled, Is.True);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenDirtyScenesBeforeReloadCannotBeSaved_ReturnsFailure()
        {
            // Verifies reload does not proceed when any dirty open Scene cannot be saved first.
            const string DirtyScenePath = "Assets/Scenes/DirtyScene.unity";
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: false) },
                _ => (true, ChangedTime, 20),
                () => new[] { DirtyScenePath },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.False);
            Assert.That(result.Message, Does.Contain("cannot save dirty Scene files"));
            Assert.That(result.ScenePaths, Is.EqualTo(new[] { DirtyScenePath }));
            Assert.That(reloadWasCalled, Is.False);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenSceneUnchanged_DoesNotSaveOrReload()
        {
            // Verifies unchanged Scene fingerprints do not trigger compile preflight work.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveDirtyOpenScenesWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, SavedTime, 10),
                () =>
                {
                    saveDirtyOpenScenesWasCalled = true;
                    return Array.Empty<string>();
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(saveDirtyOpenScenesWasCalled, Is.False);
            Assert.That(reloadWasCalled, Is.False);
        }

        [Test]
        public void FocusReturnService_WhenFocusIsLost_HoldsAutoRefreshOnce()
        {
            // Verifies focus loss suspends Unity Auto Refresh only once per unfocused interval.
            bool autoRefreshHeld = false;
            int disallowCallCount = 0;
            int allowCallCount = 0;
            ExternalAssetFocusReturnService service = CreateFocusReturnService(
                () => autoRefreshHeld,
                isHeld => autoRefreshHeld = isHeld,
                () => false,
                () => disallowCallCount++,
                () => allowCallCount++,
                () => { });

            service.HandleFocusChanged(false);
            service.HandleFocusChanged(false);

            Assert.That(autoRefreshHeld, Is.True);
            Assert.That(disallowCallCount, Is.EqualTo(1));
            Assert.That(allowCallCount, Is.EqualTo(0));
        }

        [Test]
        public void FocusReturnService_WhenFocusReturns_RunsPreflightBeforeReleasingAutoRefresh()
        {
            // Verifies focus return resolves editor state before Unity Auto Refresh resumes.
            bool autoRefreshHeld = true;
            List<string> events = new List<string>();
            ExternalAssetFocusReturnService service = CreateFocusReturnService(
                () => autoRefreshHeld,
                isHeld => autoRefreshHeld = isHeld,
                () => true,
                () => events.Add("disallow"),
                () => events.Add("allow"),
                () => events.Add("preflight"));

            service.HandleFocusChanged(true);

            Assert.That(autoRefreshHeld, Is.False);
            Assert.That(events, Is.EqualTo(new[] { "preflight", "allow" }));
        }

        [Test]
        public void FocusReturnService_WhenStartupFindsHeldAutoRefreshAndEditorIsFocused_RunsPreflightBeforeReleasingIt()
        {
            // Verifies focused startup recovery resolves editor state before releasing a reload-surviving hold.
            bool autoRefreshHeld = true;
            List<string> events = new List<string>();
            ExternalAssetFocusReturnService service = CreateFocusReturnService(
                () => autoRefreshHeld,
                isHeld => autoRefreshHeld = isHeld,
                () => true,
                () => events.Add("disallow"),
                () => events.Add("allow"),
                () => events.Add("preflight"));

            service.RestoreAutoRefreshIfHeld();

            Assert.That(autoRefreshHeld, Is.False);
            Assert.That(events, Is.EqualTo(new[] { "preflight", "allow" }));
        }

        [Test]
        public void FocusReturnService_WhenStartupFindsHeldAutoRefreshAndEditorIsUnfocused_KeepsAutoRefreshHeld()
        {
            // Verifies unfocused startup recovery keeps Auto Refresh suspended until focus returns.
            bool autoRefreshHeld = true;
            int allowCallCount = 0;
            int preflightCallCount = 0;
            ExternalAssetFocusReturnService service = CreateFocusReturnService(
                () => autoRefreshHeld,
                isHeld => autoRefreshHeld = isHeld,
                () => false,
                () => { },
                () => allowCallCount++,
                () => preflightCallCount++);

            service.RestoreAutoRefreshIfHeld();

            Assert.That(autoRefreshHeld, Is.True);
            Assert.That(allowCallCount, Is.EqualTo(0));
            Assert.That(preflightCallCount, Is.EqualTo(0));
        }

        [Test]
        public void FocusReturnService_WhenPreflightThrows_StillReleasesAutoRefresh()
        {
            // Verifies Auto Refresh is released even when focus-return preflight fails fast.
            bool autoRefreshHeld = true;
            int allowCallCount = 0;
            ExternalAssetFocusReturnService service = CreateFocusReturnService(
                () => autoRefreshHeld,
                isHeld => autoRefreshHeld = isHeld,
                () => true,
                () => { },
                () => allowCallCount++,
                () => throw new InvalidOperationException("preflight failed"));

            Assert.Throws<InvalidOperationException>(() => service.HandleFocusChanged(true));
            Assert.That(autoRefreshHeld, Is.False);
            Assert.That(allowCallCount, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotSessionStore_WhenSnapshotsRoundTrip_PreservesFingerprints()
        {
            // Verifies focus-return snapshots survive a domain reload through JSON session storage.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
            snapshots[ScenePath] = (true, SavedTime, 10);
            const string MissingScenePath = "Assets/Scenes/MissingScene.unity";
            snapshots[MissingScenePath] = (false, DateTime.MinValue, 0);

            string json = ExternalAssetSnapshotSessionStore.SerializeSnapshots(snapshots);
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> restored =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);

            ExternalAssetSnapshotSessionStore.RestoreSnapshots(restored, json);

            Assert.That(restored.Count, Is.EqualTo(2));
            Assert.That(restored[ScenePath].Exists, Is.True);
            Assert.That(restored[ScenePath].LastWriteTimeUtc, Is.EqualTo(SavedTime));
            Assert.That(restored[ScenePath].Length, Is.EqualTo(10));
            Assert.That(restored[MissingScenePath].Exists, Is.False);
        }

        [Test]
        public void SnapshotSessionStore_WhenJsonIsEmpty_ClearsSnapshots()
        {
            // Verifies empty session data clears stale snapshots after normal startup.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();

            ExternalAssetSnapshotSessionStore.RestoreSnapshots(snapshots, "");

            Assert.That(snapshots, Is.Empty);
        }

        [Test]
        public void CreatePrefabStageReopenContext_WhenInstanceIsValid_PreservesContext()
        {
            // Verifies valid in-context Prefab Stage reopen data is preserved.
            GameObject openedFromInstanceObject = new GameObject("OpenedFromInstanceObject");
            try
            {
                (GameObject OpenedFromInstanceObject, PrefabStage.Mode Mode) context =
                    ExternalSceneChangeTracker.CreatePrefabStageReopenContext(
                        openedFromInstanceObject,
                        PrefabStage.Mode.InContext,
                        _ => true);

                Assert.That(context.OpenedFromInstanceObject, Is.SameAs(openedFromInstanceObject));
                Assert.That(context.Mode, Is.EqualTo(PrefabStage.Mode.InContext));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(openedFromInstanceObject);
            }
        }

        [Test]
        public void CreatePrefabStageReopenContext_WhenInstanceIsInvalid_FallsBackToIsolation()
        {
            // Verifies invalid in-context Prefab Stage reopen data cannot reach OpenPrefab.
            GameObject openedFromInstanceObject = new GameObject("OpenedFromInstanceObject");
            try
            {
                (GameObject OpenedFromInstanceObject, PrefabStage.Mode Mode) context =
                    ExternalSceneChangeTracker.CreatePrefabStageReopenContext(
                        openedFromInstanceObject,
                        PrefabStage.Mode.InContext,
                        _ => false);

                Assert.That(context.OpenedFromInstanceObject, Is.Null);
                Assert.That(context.Mode, Is.EqualTo(PrefabStage.Mode.InIsolation));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(openedFromInstanceObject);
            }
        }

        [Test]
        public void CreatePrefabStageReopenContext_WhenInstanceIsMissing_UsesIsolation()
        {
            // Verifies missing Prefab Stage context reopens without invalid InContext arguments.
            (GameObject OpenedFromInstanceObject, PrefabStage.Mode Mode) context =
                ExternalSceneChangeTracker.CreatePrefabStageReopenContext(
                    null,
                    PrefabStage.Mode.InContext,
                    _ => true);

            Assert.That(context.OpenedFromInstanceObject, Is.Null);
            Assert.That(context.Mode, Is.EqualTo(PrefabStage.Mode.InIsolation));
        }

        private static Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> CreateSnapshots()
        {
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
            snapshots[ScenePath] = (true, SavedTime, 10);
            return snapshots;
        }

        private static ExternalAssetFocusReturnService CreateFocusReturnService(
            Func<bool> getAutoRefreshHeld,
            Action<bool> setAutoRefreshHeld,
            Func<bool> isEditorFocused,
            Action disallowAutoRefresh,
            Action allowAutoRefresh,
            Action resolveFocusReturnChanges)
        {
            return new ExternalAssetFocusReturnService(
                getAutoRefreshHeld,
                setAutoRefreshHeld,
                isEditorFocused,
                disallowAutoRefresh,
                allowAutoRefresh,
                resolveFocusReturnChanges);
        }
    }
}
