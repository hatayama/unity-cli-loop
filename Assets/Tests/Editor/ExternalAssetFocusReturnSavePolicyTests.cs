using System;
using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests which dirty assets focus return is allowed to save, without touching Unity Scene APIs.
    /// </summary>
    public sealed class ExternalAssetFocusReturnSavePolicyTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string OtherScenePath = "Assets/Scenes/AdditiveScene.unity";
        private static readonly DateTime SavedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime ChangedTime = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);
        private static readonly (bool Exists, DateTime LastWriteTimeUtc, long Length) SavedFingerprint =
            (true, SavedTime, 10);
        private static readonly (bool Exists, DateTime LastWriteTimeUtc, long Length) ChangedFingerprint =
            (true, ChangedTime, 20);
        private static readonly (bool Exists, DateTime LastWriteTimeUtc, long Length) MissingFingerprint =
            (false, DateTime.MinValue, 0);

        [Test]
        public void SelectDirtyAssetsToSave_WhenDirtyAssetFileIsUnchanged_DoesNotSelectIt()
        {
            // Verifies plain unsaved editor work survives focus return when nothing changed on disk.
            string[] selected = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[] { (AssetPath: ScenePath, IsDirty: true) },
                CreateSnapshots(),
                _ => SavedFingerprint);

            Assert.That(selected, Is.Empty);
        }

        [Test]
        public void SelectDirtyAssetsToSave_WhenDirtyAssetFileChangedOnDisk_SelectsIt()
        {
            // Verifies the in-memory state wins only when the file was replaced while unfocused.
            string[] selected = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[] { (AssetPath: ScenePath, IsDirty: true) },
                CreateSnapshots(),
                _ => ChangedFingerprint);

            Assert.That(selected, Is.EqualTo(new[] { ScenePath }));
        }

        [Test]
        public void SelectDirtyAssetsToSave_WhenDirtyAssetFileIsMissing_SelectsIt()
        {
            // Verifies a dirty asset whose file disappeared on disk is written back from memory.
            string[] selected = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[] { (AssetPath: ScenePath, IsDirty: true) },
                CreateSnapshots(),
                _ => MissingFingerprint);

            Assert.That(selected, Is.EqualTo(new[] { ScenePath }));
        }

        [Test]
        public void SelectDirtyAssetsToSave_WhenCleanAssetFileChangedOnDisk_DoesNotSelectIt()
        {
            // Verifies clean assets are left to the reload path instead of being saved.
            string[] selected = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[] { (AssetPath: ScenePath, IsDirty: false) },
                CreateSnapshots(),
                _ => ChangedFingerprint);

            Assert.That(selected, Is.Empty);
        }

        [Test]
        public void SelectDirtyAssetsToSave_WhenDirtyAssetHasNoSnapshot_DoesNotSelectIt()
        {
            // Verifies an untracked asset is never saved because no external change can be proven for it.
            string[] selected = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[] { (AssetPath: ScenePath, IsDirty: true) },
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal),
                _ => ChangedFingerprint);

            Assert.That(selected, Is.Empty);
        }

        [Test]
        public void SelectDirtyAssetsToSave_WithMixedAssets_SelectsOnlyDirtyChangedOnesInOrder()
        {
            // Verifies a dirty unchanged Scene stays unsaved even when a sibling dirty Scene changed on disk.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots = CreateSnapshots();
            snapshots[OtherScenePath] = SavedFingerprint;

            string[] selected = ExternalAssetFocusReturnSavePolicy.SelectDirtyAssetsToSave(
                new[]
                {
                    (AssetPath: ScenePath, IsDirty: true),
                    (AssetPath: OtherScenePath, IsDirty: true)
                },
                snapshots,
                assetPath => assetPath == OtherScenePath ? ChangedFingerprint : SavedFingerprint);

            Assert.That(selected, Is.EqualTo(new[] { OtherScenePath }));
        }

        private static Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> CreateSnapshots()
        {
            return new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal)
            {
                [ScenePath] = SavedFingerprint
            };
        }
    }
}
