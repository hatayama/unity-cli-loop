using System;
using System.Collections.Generic;
using NUnit.Framework;

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

        private static Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> CreateSnapshots()
        {
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal);
            snapshots[ScenePath] = (true, SavedTime, 10);
            return snapshots;
        }
    }
}
