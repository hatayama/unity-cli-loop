using NUnit.Framework;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Tests for restoring Enter Play Mode settings changed by DomainReloadDisableScope.
    /// </summary>
    public class DomainReloadDisableScopeTests
    {
        private bool _originalEnabled;
        private EnterPlayModeOptions _originalOptions;
        private McpEditorSettingsData _originalSettings;

        [SetUp]
        public void SetUp()
        {
            _originalEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalOptions = EditorSettings.enterPlayModeOptions;
            _originalSettings = McpEditorSettings.GetSettings();

            DomainReloadDisableScopeRecovery.ClearPendingRestore();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSettings.enterPlayModeOptionsEnabled = _originalEnabled;
            EditorSettings.enterPlayModeOptions = _originalOptions;
            McpEditorSettings.SaveSettings(_originalSettings);
        }

        [Test]
        public void Dispose_RestoresOriginalSettingsAndClearsPendingRestore()
        {
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            using (DomainReloadDisableScope scope = new DomainReloadDisableScope())
            {
                Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
                Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload));
                Assert.That(McpEditorSettings.GetSettings().domainReloadDisableScopeRestorePending, Is.True);
            }

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(McpEditorSettings.GetSettings().domainReloadDisableScopeRestorePending, Is.False);
        }

        [Test]
        public void RestoreIfPending_RestoresOriginalSettings_WhenScopeWasAbandoned()
        {
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            DomainReloadDisableScope scope = new DomainReloadDisableScope();
            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.DisableDomainReload));

            DomainReloadDisableScopeRecovery.RestoreIfPending();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(McpEditorSettings.GetSettings().domainReloadDisableScopeRestorePending, Is.False);
            System.GC.KeepAlive(scope);
        }

        [Test]
        public void Constructor_RestoresPendingOriginalSettings_BeforeSavingNewRun()
        {
            SetEnterPlayModeSettings(false, EnterPlayModeOptions.None);

            DomainReloadDisableScope abandonedScope = new DomainReloadDisableScope();
            Assert.That(McpEditorSettings.GetSettings().domainReloadDisableScopeRestorePending, Is.True);

            DomainReloadDisableScope nextScope = new DomainReloadDisableScope();
            McpEditorSettingsData settings = McpEditorSettings.GetSettings();

            Assert.That(settings.domainReloadDisableScopeOriginalOptionsEnabled, Is.False);
            Assert.That(settings.domainReloadDisableScopeOriginalOptions, Is.EqualTo((int)EnterPlayModeOptions.None));

            nextScope.Dispose();

            Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.False);
            Assert.That(EditorSettings.enterPlayModeOptions, Is.EqualTo(EnterPlayModeOptions.None));
            Assert.That(McpEditorSettings.GetSettings().domainReloadDisableScopeRestorePending, Is.False);
            System.GC.KeepAlive(abandonedScope);
        }

        private static void SetEnterPlayModeSettings(bool enabled, EnterPlayModeOptions options)
        {
            EditorSettings.enterPlayModeOptionsEnabled = enabled;
            EditorSettings.enterPlayModeOptions = options;
        }
    }
}
