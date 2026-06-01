using System.IO;

using NUnit.Framework;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Test fixture that verifies Enter Play Mode settings recovery for DomainReloadDisableScope.
    /// </summary>
    public sealed class DomainReloadDisableScopeTests
    {
        private static readonly string MarkerFilePath = DomainReloadDisableScopeRecovery.MarkerFilePathForTests;
        private static readonly string TempFilePath = DomainReloadDisableScopeRecovery.TempFilePathForTests;

        private bool _originalEnabled;
        private EnterPlayModeOptions _originalOptions;
        private bool _markerFileExisted;
        private string _markerFileContent;
        private bool _tempFileExisted;
        private string _tempFileContent;

        [SetUp]
        public void SetUp()
        {
            _originalEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalOptions = EditorSettings.enterPlayModeOptions;
            _markerFileExisted = File.Exists(MarkerFilePath);
            _markerFileContent = _markerFileExisted ? File.ReadAllText(MarkerFilePath) : string.Empty;
            _tempFileExisted = File.Exists(TempFilePath);
            _tempFileContent = _tempFileExisted ? File.ReadAllText(TempFilePath) : string.Empty;

            DomainReloadDisableScope.ResetActiveScopeCountForTests();
            DomainReloadDisableScopeRecovery.ClearPendingRestoreForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSettings.enterPlayModeOptionsEnabled = _originalEnabled;
            EditorSettings.enterPlayModeOptions = _originalOptions;
            DomainReloadDisableScope.ResetActiveScopeCountForTests();
            DomainReloadDisableScopeRecovery.ClearPendingRestoreForTests();
            RestoreFile(MarkerFilePath, _markerFileExisted, _markerFileContent);
            RestoreFile(TempFilePath, _tempFileExisted, _tempFileContent);
        }

        [Test]
        public void Dispose_RestoresOriginalSettingsAndDeletesMarkerFile()
        {
            // Verifies that a completed scope restores settings and removes its recovery marker.
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            using (DomainReloadDisableScope scope = new DomainReloadDisableScope())
            {
                Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
                Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload));
                Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.True);
            }

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.False);
        }

        [Test]
        public void Dispose_KeepsDomainReloadDisabled_UntilLastNestedScopeDisposes()
        {
            // Verifies that nested scopes restore settings only after the final scope exits.
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            DomainReloadDisableScope outerScope = new DomainReloadDisableScope();
            DomainReloadDisableScope innerScope = new DomainReloadDisableScope();

            innerScope.Dispose();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.True);

            outerScope.Dispose();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.False);
        }

        [Test]
        public void RestoreIfPending_RestoresOriginalSettings_WhenScopeWasAbandoned()
        {
            // Verifies that a marker left by an abandoned scope can restore the original settings.
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            DomainReloadDisableScope scope = new DomainReloadDisableScope();
            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload));

            DomainReloadDisableScopeRecovery.RestoreIfPending();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.False);
            System.GC.KeepAlive(scope);
        }

        [Test]
        public void Constructor_RestoresStaleMarker_BeforeSavingNewRunMarker()
        {
            // Verifies that stale recovery state is consumed before the next run records its baseline.
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            DomainReloadDisableScope abandonedScope = new DomainReloadDisableScope();
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.True);
            DomainReloadDisableScope.ResetActiveScopeCountForTests();

            DomainReloadDisableScope nextScope = new DomainReloadDisableScope();
            DomainReloadDisableScopeRecoveryData markerData = DomainReloadDisableScopeRecovery.ReadMarkerDataForTests();

            Assert.That(markerData.originalOptionsEnabled, Is.False);
            Assert.That(markerData.originalOptions, Is.EqualTo((int)EnterPlayModeOptions.None));

            nextScope.Dispose();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.False);
            System.GC.KeepAlive(abandonedScope);
        }

        [Test]
        public void Constructor_RestoresStaleMarker_WhenPreviousScopeWasAbandonedInSameEditorSession()
        {
            // Verifies same-session recovery after the abandoned scope is no longer alive.
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            System.WeakReference abandonedScopeReference = CreateAbandonedScopeReference();
            CollectGarbage();
            Assert.That(abandonedScopeReference.IsAlive, Is.False);

            DomainReloadDisableScope nextScope = new DomainReloadDisableScope();
            DomainReloadDisableScopeRecoveryData markerData = DomainReloadDisableScopeRecovery.ReadMarkerDataForTests();

            Assert.That(markerData.originalOptionsEnabled, Is.False);
            Assert.That(markerData.originalOptions, Is.EqualTo((int)EnterPlayModeOptions.None));

            nextScope.Dispose();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.False);
        }

        [Test]
        public void RestoreIfPending_LeavesNoMarkerFile_AfterSuccessfulRestore()
        {
            // Verifies that successful recovery deletes the marker instead of saving a cleared state.
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);
            DomainReloadDisableScope scope = new DomainReloadDisableScope();

            DomainReloadDisableScopeRecovery.RestoreIfPending();

            Assert.That(File.Exists(MarkerFilePath), Is.False);
            Assert.That(File.Exists(TempFilePath), Is.False);
            System.GC.KeepAlive(scope);
        }

        private static void SetEnterPlayModeSettings(bool enabled, EnterPlayModeOptions options)
        {
            EditorSettings.enterPlayModeOptionsEnabled = enabled;
            EditorSettings.enterPlayModeOptions = options;
        }

        private static System.WeakReference CreateAbandonedScopeReference()
        {
            DomainReloadDisableScope scope = new DomainReloadDisableScope();
            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload));
            Assert.That(DomainReloadDisableScopeRecovery.HasPendingRestoreForTests(), Is.True);
            return new System.WeakReference(scope);
        }

        private static void CollectGarbage()
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
        }

        private static void RestoreFile(string filePath, bool fileExisted, string fileContent)
        {
            if (!fileExisted)
            {
                return;
            }

            string directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(filePath, fileContent);
        }
    }
}
