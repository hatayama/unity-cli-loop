using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the dedicated V3 custom tool migration wizard.
    /// </summary>
    public sealed class ThirdPartyToolMigrationWizardWindowTests
    {
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void ShouldStartInitialRefresh_ReturnsExpectedValue(
            bool shouldRefreshAfterCreateGui,
            bool shouldAutoScanThirdPartyToolMigration,
            bool expected)
        {
            // Verifies that automatic scanning requires both auto-open intent and the session scan flag.
            bool shouldStartInitialRefresh =
                ThirdPartyToolMigrationWizardWindow.ShouldStartInitialRefresh(
                    shouldRefreshAfterCreateGui,
                    shouldAutoScanThirdPartyToolMigration);

            Assert.That(shouldStartInitialRefresh, Is.EqualTo(expected));
        }

        [TestCase(
            1,
            "1 file needs V3 custom tool migration.\n" +
            "The Unity Console is showing errors because this file still uses the old custom tool API.\n\n" +
            "Click Migrate to update it automatically. The errors should disappear after migration.")]
        [TestCase(
            3,
            "3 files need V3 custom tool migration.\n" +
            "The Unity Console is showing errors because these files still use the old custom tool API.\n\n" +
            "Click Migrate to update them automatically. The errors should disappear after migration.")]
        public void GetMigrationStatusText_WhenTargetsExist_ReturnsFileCount(
            int fileCount,
            string expectedText)
        {
            // Verifies that the migration wizard summarizes detected V2 custom tool files.
            string text = ThirdPartyToolMigrationWizardWindow.GetMigrationStatusText(fileCount);

            Assert.That(text, Is.EqualTo(expectedText));
        }

        [Test]
        public void GetMigrationProgressText_WhenProgressExists_ReturnsCheckCount()
        {
            // Verifies that the migration wizard reports scan progress while migration targets are unknown.
            ThirdPartyToolMigrationProgress progress = new(3, 12);

            string text = ThirdPartyToolMigrationWizardWindow.GetMigrationProgressText(progress);

            Assert.That(
                text,
                Is.EqualTo("Scanning project for V3 custom tool migration...\n3/12 checks complete."));
        }

        [TestCase(false, true, "Migrate")]
        [TestCase(true, true, "Migrating...")]
        [TestCase(false, false, "Nothing to migrate")]
        public void GetMigrationButtonText_ReturnsExpectedLabel(
            bool isMigrating,
            bool hasMigrationTargets,
            string expectedLabel)
        {
            // Verifies that the migration action communicates its current state.
            string label = ThirdPartyToolMigrationWizardWindow.GetMigrationButtonText(
                isMigrating,
                hasMigrationTargets);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [Test]
        public void PrepareForOpen_PopulatesWindowStateBeforeShowing()
        {
            // Verifies that auto-opened migration windows can request an initial session-gated scan.
            ThirdPartyToolMigrationWizardWindow window =
                ScriptableObject.CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            try
            {
                Rect position = new(12f, 34f, 360f, 220f);

                ThirdPartyToolMigrationWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Migration",
                    position,
                    true);

                SerializedObject serializedWindow = new(window);
                SerializedProperty refreshProperty =
                    serializedWindow.FindProperty("_shouldRefreshAfterCreateGui");

                Assert.That(window.titleContent.text, Is.EqualTo("Unity CLI Loop Migration"));
                Assert.That(window.position, Is.EqualTo(position));
                Assert.That(window.minSize, Is.EqualTo(new Vector2(360f, 120f)));
                Assert.That(refreshProperty, Is.Not.Null);
                Assert.That(refreshProperty.boolValue, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PrepareForOpen_WhenManualOpen_PopulatesWindowStateWithoutInitialRefresh()
        {
            // Verifies that manually opened migration windows wait for the Check button.
            ThirdPartyToolMigrationWizardWindow window =
                ScriptableObject.CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            try
            {
                Rect position = new(12f, 34f, 360f, 220f);

                ThirdPartyToolMigrationWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Migration",
                    position,
                    false);

                SerializedObject serializedWindow = new(window);
                SerializedProperty refreshProperty =
                    serializedWindow.FindProperty("_shouldRefreshAfterCreateGui");

                Assert.That(window.titleContent.text, Is.EqualTo("Unity CLI Loop Migration"));
                Assert.That(window.position, Is.EqualTo(position));
                Assert.That(window.minSize, Is.EqualTo(new Vector2(360f, 120f)));
                Assert.That(refreshProperty, Is.Not.Null);
                Assert.That(refreshProperty.boolValue, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void WithContentHeight_UsesMeasuredHeightAndPreservesCenter()
        {
            // Verifies that the migration wizard resizes vertically from measured content height.
            Rect initialRect = new(123f, 456f, 400f, 220f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 180f, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 208f)));
        }

        [Test]
        public void WithContentHeight_WhenMeasuredHeightIsSmall_ClampsToMinimumHeight()
        {
            // Verifies that content fitting keeps the migration wizard from becoming unusably short.
            Rect initialRect = new(123f, 456f, 400f, 220f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 12f, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 120f)));
        }

        [Test]
        public void WithContentHeight_WhenCurrentWidthIsWide_UsesSetupWizardWidth()
        {
            // Verifies that content fitting keeps the migration wizard at Setup Wizard width.
            Rect initialRect = new(123f, 456f, 520f, 220f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 120f, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 148f)));
        }
    }
}
