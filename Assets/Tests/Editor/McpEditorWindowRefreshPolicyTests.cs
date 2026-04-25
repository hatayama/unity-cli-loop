using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    public class McpEditorWindowRefreshPolicyTests
    {
        [Test]
        public void ShouldRefreshOnEditorUpdate_WhenRepaintIsRequested_ReturnsTrue()
        {
            RuntimeState runtimeState = new RuntimeState(needsRepaint: true);

            bool shouldRefresh = McpEditorWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(runtimeState);

            Assert.That(shouldRefresh, Is.True);
        }

        [Test]
        public void ShouldRefreshOnEditorUpdate_WhenPostCompileModeHasNoRepaintRequest_ReturnsFalse()
        {
            RuntimeState runtimeState = new RuntimeState(
                needsRepaint: false,
                isPostCompileMode: true);

            bool shouldRefresh = McpEditorWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(runtimeState);

            Assert.That(shouldRefresh, Is.False);
        }

        [Test]
        public void ShouldRunExpensiveChecks_WhenInitialPaint_ReturnsFalse()
        {
            bool shouldRun = McpEditorWindowRefreshPolicy.ShouldRunExpensiveChecks(
                McpEditorWindowRefreshMode.InitialPaint);

            Assert.That(shouldRun, Is.False);
        }

        [Test]
        public void ShouldRefreshSkillInstallState_WhenInitialPaintEvenIfRequested_ReturnsFalse()
        {
            bool shouldRefresh = McpEditorWindowRefreshPolicy.ShouldRefreshSkillInstallState(
                McpEditorWindowRefreshMode.InitialPaint,
                refreshRequested: true);

            Assert.That(shouldRefresh, Is.False);
        }

        [Test]
        public void ShouldRefreshSkillInstallState_WhenFullRefreshRequested_ReturnsTrue()
        {
            bool shouldRefresh = McpEditorWindowRefreshPolicy.ShouldRefreshSkillInstallState(
                McpEditorWindowRefreshMode.Full,
                refreshRequested: true);

            Assert.That(shouldRefresh, Is.True);
        }

        [Test]
        public void ShouldKeepToolSettingsCatalogDirty_WhenOpenRegistryUnavailable_ReturnsTrue()
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(
                showToolSettings: true,
                isRegistryAvailable: false);

            bool shouldKeepDirty = McpEditorWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData);

            Assert.That(shouldKeepDirty, Is.True);
        }

        [Test]
        public void ShouldKeepToolSettingsCatalogDirty_WhenOpenRegistryAvailable_ReturnsFalse()
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(
                showToolSettings: true,
                isRegistryAvailable: true);

            bool shouldKeepDirty = McpEditorWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData);

            Assert.That(shouldKeepDirty, Is.False);
        }

        [Test]
        public void ShouldKeepToolSettingsCatalogDirty_WhenClosedRegistryUnavailable_ReturnsFalse()
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(
                showToolSettings: false,
                isRegistryAvailable: false);

            bool shouldKeepDirty = McpEditorWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData);

            Assert.That(shouldKeepDirty, Is.False);
        }

        [Test]
        public void ShouldStartToolSettingsRegistryWarmup_WhenNotScheduledAndBelowMaxAttempts_ReturnsTrue()
        {
            bool shouldStart = McpEditorWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                isAlreadyScheduled: false,
                attemptCount: 4,
                maxAttempts: 5);

            Assert.That(shouldStart, Is.True);
        }

        [Test]
        public void ShouldStartToolSettingsRegistryWarmup_WhenAlreadyScheduled_ReturnsFalse()
        {
            bool shouldStart = McpEditorWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                isAlreadyScheduled: true,
                attemptCount: 0,
                maxAttempts: 5);

            Assert.That(shouldStart, Is.False);
        }

        [Test]
        public void ShouldStartToolSettingsRegistryWarmup_WhenMaxAttemptsReached_ReturnsFalse()
        {
            bool shouldStart = McpEditorWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                isAlreadyScheduled: false,
                attemptCount: 5,
                maxAttempts: 5);

            Assert.That(shouldStart, Is.False);
        }

        [TestCase(0, 0.05)]
        [TestCase(1, 0.1)]
        [TestCase(2, 0.2)]
        [TestCase(3, 0.4)]
        [TestCase(4, 0.8)]
        [TestCase(5, 0.8)]
        public void CalculateToolSettingsRegistryWarmupDelaySeconds_UsesExponentialBackoffWithCap(
            int attemptCount,
            double expectedDelaySeconds)
        {
            double delaySeconds = McpEditorWindowRefreshPolicy.CalculateToolSettingsRegistryWarmupDelaySeconds(
                initialDelaySeconds: 0.05,
                maxDelaySeconds: 0.8,
                attemptCount);

            Assert.That(delaySeconds, Is.EqualTo(expectedDelaySeconds).Within(0.0001));
        }

        private static ToolSettingsSectionData CreateToolSettingsData(
            bool showToolSettings,
            bool isRegistryAvailable)
        {
            return new ToolSettingsSectionData(
                showToolSettings,
                allowThirdPartyTools: false,
                DynamicCodeSecurityLevel.Restricted,
                System.Array.Empty<ToolToggleItem>(),
                System.Array.Empty<ToolToggleItem>(),
                isRegistryAvailable);
        }
    }
}
