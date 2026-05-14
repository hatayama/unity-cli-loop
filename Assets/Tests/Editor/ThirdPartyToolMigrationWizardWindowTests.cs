using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
                Assert.That(window.minSize, Is.EqualTo(new Vector2(300f, 120f)));
                Assert.That(refreshProperty, Is.Not.Null);
                Assert.That(refreshProperty.boolValue, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void WithContentSize_UsesMeasuredSizeAndPreservesCenter()
        {
            // Verifies that the migration wizard resizes from measured content size.
            Rect initialRect = new(123f, 456f, 400f, 220f);
            Vector2 contentSize = new(260f, 180f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(300f, 208f)));
        }

        [Test]
        public void WithContentSize_WhenMeasuredSizeIsSmall_ClampsToMinimumSize()
        {
            // Verifies that content fitting keeps the migration wizard from becoming unusably small.
            Rect initialRect = new(123f, 456f, 400f, 220f);
            Vector2 contentSize = new(12f, 12f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(300f, 120f)));
        }

        [Test]
        public void WithContentSize_WhenMeasuredWidthIsLarge_UsesMeasuredWidth()
        {
            // Verifies that content fitting still expands when migration copy needs more width.
            Rect initialRect = new(123f, 456f, 300f, 220f);
            Vector2 contentSize = new(380f, 120f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(398f, 148f)));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenTextHasExplicitLineBreak_ReturnsMeasuredWidth()
        {
            // Verifies that manually wrapped copy keeps the longest explicit line visible.
            float width = ThirdPartyToolMigrationWizardWindow.SelectPreferredTextWidth(
                laidOutWidth: 220f,
                measuredWidth: 340f,
                lineCount: 3,
                whiteSpace: WhiteSpace.Normal,
                hasExplicitLineBreak: true);

            Assert.That(width, Is.EqualTo(340f));
        }
    }
}
