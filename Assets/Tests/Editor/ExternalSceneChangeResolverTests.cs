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
            bool saveWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: false) },
                _ => (true, ChangedTime, 20),
                _ =>
                {
                    saveWasCalled = true;
                    return true;
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(saveWasCalled, Is.False);
            Assert.That(reloadWasCalled, Is.True);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenDirtySceneChangedAndReloadEnabled_SavesScene()
        {
            // Verifies default compile behavior saves dirty externally changed Scenes before continuing.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, ChangedTime, 20),
                _ =>
                {
                    saveWasCalled = true;
                    return true;
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(saveWasCalled, Is.True);
            Assert.That(reloadWasCalled, Is.False);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenStopPolicyIsEnabled_ReturnsFailureWithoutMutation()
        {
            // Verifies stop-on-external-scene-changes prevents automatic save or reload.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, ChangedTime, 20),
                _ =>
                {
                    saveWasCalled = true;
                    return true;
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
            Assert.That(saveWasCalled, Is.False);
            Assert.That(reloadWasCalled, Is.False);
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenDirtySaveFails_ReturnsFailure()
        {
            // Verifies compile stops when a dirty externally changed Scene cannot be saved.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, ChangedTime, 20),
                _ => false,
                () => true);

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.False);
            Assert.That(result.Message, Does.Contain("could not be saved or reloaded"));
            Assert.That(result.ScenePaths, Is.EqualTo(new[] { ScenePath }));
        }

        [Test]
        public void ResolveExternalSceneChanges_WhenSceneUnchanged_DoesNotSaveOrReload()
        {
            // Verifies unchanged Scene fingerprints do not trigger compile preflight work.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            bool saveWasCalled = false;
            bool reloadWasCalled = false;
            ExternalSceneChangeResolver resolver = new ExternalSceneChangeResolver(
                snapshots,
                () => new[] { (AssetPath: ScenePath, IsDirty: true) },
                _ => (true, SavedTime, 10),
                _ =>
                {
                    saveWasCalled = true;
                    return true;
                },
                () =>
                {
                    reloadWasCalled = true;
                    return true;
                });

            (bool CanProceed, string Message, string[] ScenePaths) result = resolver.ResolveExternalSceneChanges(
                reloadExternalSceneChanges: true);

            Assert.That(result.CanProceed, Is.True);
            Assert.That(saveWasCalled, Is.False);
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
