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
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void ShouldAutoShowForMigrationTargets_ReturnsDetectionResult(
            bool hasMigrationTargets,
            bool expected)
        {
            // Verifies that migration startup is controlled only by migration target detection.
            bool shouldAutoShow =
                ThirdPartyToolMigrationWizardWindow.ShouldAutoShowForMigrationTargets(hasMigrationTargets);

            Assert.That(shouldAutoShow, Is.EqualTo(expected));
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

        [TestCase(false, "Migrate")]
        [TestCase(true, "Migrating...")]
        public void GetMigrationButtonText_ReturnsExpectedLabel(
            bool isMigrating,
            string expectedLabel)
        {
            // Verifies that the migration action communicates its current state.
            string label = ThirdPartyToolMigrationWizardWindow.GetMigrationButtonText(isMigrating);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [Test]
        public void PrepareForOpen_PopulatesWindowStateBeforeShowing()
        {
            // Verifies that startup-created migration windows can preview immediately after CreateGUI.
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
                Assert.That(window.minSize, Is.EqualTo(new Vector2(360f, 220f)));
                Assert.That(refreshProperty, Is.Not.Null);
                Assert.That(refreshProperty.boolValue, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
