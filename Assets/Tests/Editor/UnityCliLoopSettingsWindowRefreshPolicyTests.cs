using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Settings Window Refresh Policy behavior.
    /// </summary>
    public class UnityCliLoopSettingsWindowRefreshPolicyTests
    {
        [Test]
        public void ShouldRefreshOnEditorUpdate_WhenRepaintIsRequested_ReturnsTrue()
        {
            // Verifies that an explicit repaint request triggers the editor update refresh.
            RuntimeState runtimeState = new(needsRepaint: true);

            bool shouldRefresh = UnityCliLoopSettingsWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(runtimeState);

            Assert.That(shouldRefresh, Is.True);
        }

        [Test]
        public void ShouldRefreshOnEditorUpdate_WhenPostCompileModeHasNoRepaintRequest_ReturnsFalse()
        {
            // Verifies that post-compile mode alone does not force a refresh.
            RuntimeState runtimeState = new(
                needsRepaint: false,
                isPostCompileMode: true);

            bool shouldRefresh = UnityCliLoopSettingsWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(runtimeState);

            Assert.That(shouldRefresh, Is.False);
        }

        [Test]
        public void ShouldRunExpensiveChecks_WhenInitialPaint_ReturnsFalse()
        {
            // Verifies that the first paint skips expensive refresh work.
            bool shouldRun = UnityCliLoopSettingsWindowRefreshPolicy.ShouldRunExpensiveChecks(
                UnityCliLoopSettingsWindowRefreshMode.InitialPaint);

            Assert.That(shouldRun, Is.False);
        }

        [Test]
        public void ShouldRefreshSkillInstallState_WhenInitialPaintEvenIfRequested_ReturnsFalse()
        {
            // Verifies that initial paint does not run skill freshness checks.
            bool shouldRefresh = UnityCliLoopSettingsWindowRefreshPolicy.ShouldRefreshSkillInstallState(
                UnityCliLoopSettingsWindowRefreshMode.InitialPaint,
                refreshRequested: true);

            Assert.That(shouldRefresh, Is.False);
        }

        [Test]
        public void ShouldRefreshSkillInstallState_WhenFullRefreshRequested_ReturnsTrue()
        {
            // Verifies that full refresh honors an explicit skill refresh request.
            bool shouldRefresh = UnityCliLoopSettingsWindowRefreshPolicy.ShouldRefreshSkillInstallState(
                UnityCliLoopSettingsWindowRefreshMode.Full,
                refreshRequested: true);

            Assert.That(shouldRefresh, Is.True);
        }

        [Test]
        public void ShouldKeepToolSettingsCatalogDirty_WhenOpenRegistryUnavailable_ReturnsTrue()
        {
            // Verifies that an open tool section stays dirty while the registry is unavailable.
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(
                showToolSettings: true,
                isRegistryAvailable: false);

            bool shouldKeepDirty = UnityCliLoopSettingsWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData);

            Assert.That(shouldKeepDirty, Is.True);
        }

        [Test]
        public void ShouldKeepToolSettingsCatalogDirty_WhenOpenRegistryAvailable_ReturnsFalse()
        {
            // Verifies that an available registry clears the dirty catalog state.
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(
                showToolSettings: true,
                isRegistryAvailable: true);

            bool shouldKeepDirty = UnityCliLoopSettingsWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData);

            Assert.That(shouldKeepDirty, Is.False);
        }

        [Test]
        public void ShouldKeepToolSettingsCatalogDirty_WhenClosedRegistryUnavailable_ReturnsFalse()
        {
            // Verifies that a closed tool section does not keep retrying registry refreshes.
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(
                showToolSettings: false,
                isRegistryAvailable: false);

            bool shouldKeepDirty = UnityCliLoopSettingsWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData);

            Assert.That(shouldKeepDirty, Is.False);
        }

        [Test]
        public void ShouldStartToolSettingsRegistryWarmup_WhenNotScheduledAndBelowMaxAttempts_ReturnsTrue()
        {
            // Verifies that registry warmup starts while the retry budget remains.
            bool shouldStart = UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                isAlreadyScheduled: false,
                attemptCount: 4,
                maxAttempts: 5);

            Assert.That(shouldStart, Is.True);
        }

        [Test]
        public void ShouldStartToolSettingsRegistryWarmup_WhenAlreadyScheduled_ReturnsFalse()
        {
            // Verifies that an existing warmup schedule is not duplicated.
            bool shouldStart = UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                isAlreadyScheduled: true,
                attemptCount: 0,
                maxAttempts: 5);

            Assert.That(shouldStart, Is.False);
        }

        [Test]
        public void ShouldStartToolSettingsRegistryWarmup_WhenMaxAttemptsReached_ReturnsFalse()
        {
            // Verifies that registry warmup stops after the retry budget is exhausted.
            bool shouldStart = UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
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
            // Verifies that registry warmup delay doubles until the configured cap.
            double delaySeconds = UnityCliLoopSettingsWindowRefreshPolicy.CalculateToolSettingsRegistryWarmupDelaySeconds(
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
                DynamicCodeSecurityLevel.Restricted,
                System.Array.Empty<ToolToggleItem>(),
                System.Array.Empty<ToolToggleItem>(),
                isRegistryAvailable);
        }
    }
}
