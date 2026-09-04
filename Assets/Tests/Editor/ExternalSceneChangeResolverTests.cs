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
                _ =>
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
                _ =>
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
                _ =>
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
                _ =>
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
                _ =>
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
                _ =>
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
        public void ResolveExternalSceneChanges_WhenMultipleCleanScenesChanged_PassesOnlyChangedPathsToReload()
        {
            // Verifies the compile preflight reload delegate receives only the externally changed Scene paths.
            const string ChangedSceneA = "Assets/Scenes/ChangedA.unity";
            const string ChangedSceneB = "Assets/Scenes/ChangedB.unity";
            const string UnchangedScene = "Assets/Scenes/Unchanged.unity";
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
            snapshots[ChangedSceneA] = (true, SavedTime, 10);
            snapshots[ChangedSceneB] = (true, SavedTime, 10);
            snapshots[UnchangedScene] = (true, SavedTime, 10);
            string[] reloadedPaths = null;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[]
                {
                    (AssetPath: ChangedSceneA, IsDirty: false),
                    (AssetPath: ChangedSceneB, IsDirty: false),
                    (AssetPath: UnchangedScene, IsDirty: false)
                },
                assetPath => assetPath == UnchangedScene
                    ? (true, SavedTime, 10)
                    : (true, ChangedTime, 20),
                () => Array.Empty<string>(),
                paths =>
                {
                    reloadedPaths = paths;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(reloadedPaths, Is.EqualTo(new[] { ChangedSceneA, ChangedSceneB }));
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
                    ExternalPrefabStageChangeTracker.CreatePrefabStageReopenContext(
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
                    ExternalPrefabStageChangeTracker.CreatePrefabStageReopenContext(
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
        public void BuildFingerprintDiffContexts_WhenNoSnapshotExists_ReportsChangedWithoutSnapshot()
        {
            // Pins first-observation behavior used by focus-return start/end observability logging.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
            (string AssetPath, bool IsDirty)[] scenes = { (ScenePath, false) };

            object[] diffs = ExternalSceneChangeTracker.BuildFingerprintDiffContexts(
                scenes, snapshots, _ => (true, ChangedTime, 20));

            Assert.That(diffs, Has.Length.EqualTo(1));
            dynamic diff = diffs[0];
            Assert.That((string)diff.assetPath, Is.EqualTo(ScenePath));
            Assert.That((bool)diff.changed, Is.True);
            Assert.That((bool)diff.hasSnapshot, Is.False);
            Assert.That((bool)diff.snapshotExists, Is.False);
            Assert.That((bool)diff.currentExists, Is.True);
            Assert.That((long)diff.currentLength, Is.EqualTo(20));
        }

        [Test]
        public void BuildFingerprintDiffContexts_WhenFingerprintMatchesSnapshot_ReportsUnchanged()
        {
            // Pins that identical fingerprints are reported as unchanged for observability logging.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            (string AssetPath, bool IsDirty)[] scenes = { (ScenePath, true) };

            object[] diffs = ExternalSceneChangeTracker.BuildFingerprintDiffContexts(
                scenes, snapshots, _ => (true, SavedTime, 10));

            dynamic diff = diffs[0];
            Assert.That((bool)diff.hasSnapshot, Is.True);
            Assert.That((bool)diff.changed, Is.False);
            Assert.That((bool)diff.isDirty, Is.True);
        }

        [Test]
        public void BuildFingerprintDiffContexts_WhenFingerprintDiffersFromSnapshot_ReportsChanged()
        {
            // Pins that a diverged fingerprint is reported as changed for observability logging.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            (string AssetPath, bool IsDirty)[] scenes = { (ScenePath, false) };

            object[] diffs = ExternalSceneChangeTracker.BuildFingerprintDiffContexts(
                scenes, snapshots, _ => (true, ChangedTime, 20));

            dynamic diff = diffs[0];
            Assert.That((bool)diff.hasSnapshot, Is.True);
            Assert.That((bool)diff.changed, Is.True);
            Assert.That((long)diff.snapshotLength, Is.EqualTo(10));
            Assert.That((long)diff.currentLength, Is.EqualTo(20));
        }

        [Test]
        public void CreatePrefabStageReopenContext_WhenInstanceIsMissing_UsesIsolation()
        {
            // Verifies missing Prefab Stage context reopens without invalid InContext arguments.
            (GameObject OpenedFromInstanceObject, PrefabStage.Mode Mode) context =
                ExternalPrefabStageChangeTracker.CreatePrefabStageReopenContext(
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
    }
}
