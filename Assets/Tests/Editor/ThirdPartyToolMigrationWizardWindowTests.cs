using System.Threading;
using System.Threading.Tasks;

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
        [Test]
        public void ConsumeShouldStartInitialRefresh_WhenPreparedForAutoScan_ReturnsTrueWithoutSessionFlag()
        {
            // Verifies auto-open windows start scanning from the serialized refresh flag alone.
            ThirdPartyToolMigrationWizardWindow window =
                ScriptableObject.CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            try
            {
                ThirdPartyToolMigrationWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Migration",
                    new Rect(12f, 34f, 360f, 220f),
                    shouldRefreshAfterCreateGui: true);

                bool shouldStartInitialRefresh = window.ConsumeShouldStartInitialRefresh();

                Assert.That(shouldStartInitialRefresh, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ConsumeShouldStartInitialRefresh_WhenConsumedTwice_ReturnsFalseOnSecondCall()
        {
            // Verifies the initial-refresh flag is one-shot so CreateGUI cannot double-scan.
            ThirdPartyToolMigrationWizardWindow window =
                ScriptableObject.CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            try
            {
                ThirdPartyToolMigrationWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Migration",
                    new Rect(12f, 34f, 360f, 220f),
                    shouldRefreshAfterCreateGui: true);

                bool first = window.ConsumeShouldStartInitialRefresh();
                bool second = window.ConsumeShouldStartInitialRefresh();

                Assert.That(first, Is.True);
                Assert.That(second, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ConsumeShouldStartInitialRefresh_WhenManualOpen_ReturnsFalse()
        {
            // Verifies manually opened windows wait for an explicit Check instead of auto-scanning.
            ThirdPartyToolMigrationWizardWindow window =
                ScriptableObject.CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            try
            {
                ThirdPartyToolMigrationWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Migration",
                    new Rect(12f, 34f, 360f, 220f),
                    shouldRefreshAfterCreateGui: false);

                bool shouldStartInitialRefresh = window.ConsumeShouldStartInitialRefresh();

                Assert.That(shouldStartInitialRefresh, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void ShouldOpenWindowAfterAutoScan_ReturnsExpectedValue(
            bool hasMigrationTargets,
            bool isCancellationRequested,
            bool expected)
        {
            // Verifies that auto-scan opens the migration window only when preflight finds work.
            bool shouldOpenWindow = ThirdPartyToolMigrationWizardWindow.ShouldOpenWindowAfterAutoScan(
                hasMigrationTargets,
                isCancellationRequested);

            Assert.That(shouldOpenWindow, Is.EqualTo(expected));
        }

        [Test]
        public async Task RunAutoScanAsync_WhenTargetsExist_OpensWindowAndConsumesState()
        {
            // Verifies that a successful auto-scan opens the migration wizard and consumes the session flag.
            bool openedWindow = false;
            bool consumedSessionState = false;
            System.Exception loggedException = null;

            bool didOpenWindow = await ThirdPartyToolMigrationWizardWindow.RunAutoScanAsync(
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                () => openedWindow = true,
                () => consumedSessionState = true,
                ex => loggedException = ex,
                CancellationToken.None);

            Assert.That(didOpenWindow, Is.True);
            Assert.That(openedWindow, Is.True);
            Assert.That(consumedSessionState, Is.True);
            Assert.That(loggedException, Is.Null);
        }

        [Test]
        public async Task RunAutoScanAsync_WhenScanThrows_LogsExceptionAndConsumesState()
        {
            // Verifies that failed auto-scans cannot leak the session flag or crash through async void.
            bool openedWindow = false;
            bool consumedSessionState = false;
            System.InvalidOperationException expectedException = new("scan failed");
            System.Exception loggedException = null;

            bool didOpenWindow = await ThirdPartyToolMigrationWizardWindow.RunAutoScanAsync(
                _ => Task.FromException<bool>(expectedException),
                _ => Task.CompletedTask,
                () => openedWindow = true,
                () => consumedSessionState = true,
                ex => loggedException = ex,
                CancellationToken.None);

            Assert.That(didOpenWindow, Is.False);
            Assert.That(openedWindow, Is.False);
            Assert.That(consumedSessionState, Is.True);
            Assert.That(loggedException, Is.SameAs(expectedException));
        }

        [Test]
        public void GetMigrationStatusText_WhenTargetsExist_ReturnsCountOnlyMessage()
        {
            // Verifies the migration status is a brief count message with no file paths listed.
            string text = ThirdPartyToolMigrationWizardWindow.GetMigrationStatusText(12);

            Assert.That(text, Is.EqualTo("Found 12 C# files that need V3 migration."));
        }

        [Test]
        public void GetMigrationStatusText_WhenSingleTargetExists_UsesSingularNoun()
        {
            // Verifies the singular file count does not pluralize the noun.
            string text = ThirdPartyToolMigrationWizardWindow.GetMigrationStatusText(1);

            Assert.That(text, Is.EqualTo("Found 1 C# file that needs V3 migration."));
        }

        [Test]
        public void ConfirmMigrationApply_WhenDialogIsCanceled_ReturnsFalse()
        {
            // Verifies Cancel leaves migration unstarted by returning false from the confirm helper.
            bool confirmed = ThirdPartyToolMigrationWizardWindow.ConfirmMigrationApply(
                2,
                (title, message, ok, cancel) =>
                {
                    Assert.That(title, Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationConfirmDialogTitle));
                    Assert.That(message, Does.Contain("2 files will be rewritten in place."));
                    Assert.That(message, Does.Contain("VCS recommended"));
                    Assert.That(ok, Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationConfirmDialogOkText));
                    Assert.That(cancel, Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationConfirmDialogCancelText));
                    return false;
                });

            Assert.That(confirmed, Is.False);
        }

        [Test]
        public void ConfirmMigrationApply_WhenDialogIsAccepted_ReturnsTrue()
        {
            // Verifies Migrate confirmation allows the apply path to continue.
            bool confirmed = ThirdPartyToolMigrationWizardWindow.ConfirmMigrationApply(
                1,
                (_, _, _, _) => true);

            Assert.That(confirmed, Is.True);
        }

        [Test]
        public void GetMigrationProgressText_WhenPreviewProgressExists_ReturnsCheckCount()
        {
            // Verifies that the migration wizard reports scan progress while migration targets are unknown.
            ThirdPartyToolMigrationProgress progress = new(3, 12);

            string text = ThirdPartyToolMigrationWizardWindow.GetMigrationProgressText(
                progress,
                isMigrating: false);

            Assert.That(
                text,
                Is.EqualTo("Scanning C# source files for V3 custom tool API migration...\n3/12 steps complete."));
        }

        [Test]
        public void GetMigrationProgressText_WhenApplyProgressExists_ReturnsMigrationCount()
        {
            // Verifies that the migration wizard distinguishes apply progress from preview scans.
            ThirdPartyToolMigrationProgress progress = new(4, 12);

            string text = ThirdPartyToolMigrationWizardWindow.GetMigrationProgressText(
                progress,
                isMigrating: true);

            Assert.That(
                text,
                Is.EqualTo("Migrating C# source files to V3 custom tool APIs...\n4/12 steps complete."));
        }

        [TestCase(false, false, false, "Check required")]
        [TestCase(false, true, true, "Migrate")]
        [TestCase(true, true, true, "Migrating...")]
        [TestCase(false, false, true, "Nothing to migrate")]
        public void GetMigrationButtonText_ReturnsExpectedLabel(
            bool isMigrating,
            bool hasMigrationTargets,
            bool hasCheckedMigrationStatus,
            string expectedLabel)
        {
            // Verifies that the migration action communicates its current state.
            string label = ThirdPartyToolMigrationWizardWindow.GetMigrationButtonText(
                isMigrating,
                hasMigrationTargets,
                hasCheckedMigrationStatus);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(false, SkillInstallState.Missing, "Install Migration Skill")]
        [TestCase(false, SkillInstallState.Checking, "Install Migration Skill")]
        [TestCase(false, SkillInstallState.Installed, "Remove Migration Skill")]
        [TestCase(false, SkillInstallState.Outdated, "Remove Migration Skill")]
        [TestCase(true, SkillInstallState.Installed, "Updating...")]
        public void GetMigrationSkillButtonText_ReturnsExpectedLabel(
            bool isUpdating,
            SkillInstallState installState,
            string expectedLabel)
        {
            // Verifies that the migration skill action reflects install, remove, and busy states.
            string label = ThirdPartyToolMigrationWizardWindow.GetMigrationSkillButtonText(
                isUpdating,
                installState);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [Test]
        public void GetMigrationSkillPromptText_ReturnsActionablePrompt()
        {
            // Verifies that the wizard provides a copyable prompt for asking an AI agent to run the skill.
            string prompt = ThirdPartyToolMigrationWizardWindow.GetMigrationSkillPromptText();

            Assert.That(prompt, Does.Contain("v3-cli-invocation-migration"));
            Assert.That(prompt, Does.Contain("SKILL.md, Markdown, POSIX shell scripts, and PowerShell scripts"));
            Assert.That(prompt, Does.Contain("Do not change C# snippets"));
            Assert.That(prompt, Does.Not.Contain("JSON output fields"));
            Assert.That(prompt, Does.Contain("summarize changed files"));
        }

        [Test]
        public void GetMigrationSkillPromptCopyButtonText_ReturnsClearLabel()
        {
            // Verifies that the copy button label describes the clipboard action.
            string label = ThirdPartyToolMigrationWizardWindow.GetMigrationSkillPromptCopyButtonText();

            Assert.That(label, Is.EqualTo("Copy AI Prompt"));
        }

        [Test]
        public void CopyMigrationSkillPromptToClipboard_WritesPrompt()
        {
            // Verifies that the copy action places the AI prompt on the system clipboard.
            string originalClipboard = EditorGUIUtility.systemCopyBuffer;
            try
            {
                ThirdPartyToolMigrationWizardWindow.CopyMigrationSkillPromptToClipboard();

                Assert.That(
                    EditorGUIUtility.systemCopyBuffer,
                    Is.EqualTo(ThirdPartyToolMigrationWizardWindow.GetMigrationSkillPromptText()));
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = originalClipboard;
            }
        }

        [Test]
        public void Create_WhenMigrationSkillSectionExists_PlacesPromptAfterInstallButton()
        {
            // Verifies that the AI prompt is shown below the migration skill install action.
            VisualElement root = new();
            ThirdPartyToolMigrationWizardView.Create(
                root,
                () => { },
                () => { },
                _ => { },
                () => { },
                () => { });

            Button installButton = root.Query<Button>().ToList()
                .Find(button => button.text == "Install Migration Skill");
            Foldout promptFoldout = root.Query<Foldout>().ToList()
                .Find(foldout => foldout.text == "Prompt for your AI agent");

            Assert.That(installButton, Is.Not.Null);
            Assert.That(promptFoldout, Is.Not.Null);
            Assert.That(
                GetVisualElementIndex(root, promptFoldout),
                Is.GreaterThan(GetVisualElementIndex(root, installButton)));
        }

        [Test]
        public void Create_WhenMigrationSkillSectionExists_PlacesCopyButtonOutsidePromptFoldout()
        {
            // Verifies that the copy action remains available outside the collapsible prompt body.
            VisualElement root = new();
            ThirdPartyToolMigrationWizardView.Create(
                root,
                () => { },
                () => { },
                _ => { },
                () => { },
                () => { });

            Foldout promptFoldout = root.Query<Foldout>().ToList()
                .Find(foldout => foldout.text == "Prompt for your AI agent");
            Button copyButton = root.Query<Button>().ToList()
                .Find(button => button.text == "Copy AI Prompt");

            Assert.That(promptFoldout, Is.Not.Null);
            Assert.That(copyButton, Is.Not.Null);
            System.Collections.Generic.List<Button> foldoutButtons = promptFoldout.Query<Button>().ToList();
            Assert.That(foldoutButtons.Contains(copyButton), Is.False);
            Assert.That(
                GetVisualElementIndex(root, copyButton),
                Is.GreaterThan(GetVisualElementIndex(root, promptFoldout)));
        }

        [Test]
        public void ShowNoMigrationTargetsState_DisablesCheckButtonWithoutShowingCloseButton()
        {
            // Verifies that the completed migration state keeps Check visible but unavailable.
            VisualElement root = new();
            ThirdPartyToolMigrationWizardView view = ThirdPartyToolMigrationWizardView.Create(
                root,
                () => { },
                () => { },
                _ => { },
                () => { },
                () => { });

            view.ShowNoMigrationTargetsState(isMigrating: false);

            Button checkButton = root.Query<Button>().ToList()
                .Find(button => button.text == "Check");
            Button closeButton = root.Query<Button>().ToList()
                .Find(button => button.text == "Close");

            Assert.That(checkButton, Is.Not.Null);
            Assert.That(closeButton, Is.Not.Null);
            Assert.That(checkButton.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(checkButton.enabledSelf, Is.False);
            Assert.That(closeButton.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [TestCase(SkillInstallState.Installed, true)]
        [TestCase(SkillInstallState.Outdated, true)]
        [TestCase(SkillInstallState.Missing, false)]
        [TestCase(SkillInstallState.Checking, false)]
        public void ShouldRemoveMigrationSkill_ReturnsExpectedValue(
            SkillInstallState installState,
            bool expected)
        {
            // Verifies that the migration skill action removes only when files are detected.
            bool shouldRemove = ThirdPartyToolMigrationWizardWindow.ShouldRemoveMigrationSkill(installState);

            Assert.That(shouldRemove, Is.EqualTo(expected));
        }

        [TestCase(SkillInstallState.Installed, true)]
        [TestCase(SkillInstallState.Outdated, true)]
        [TestCase(SkillInstallState.Checking, true)]
        [TestCase(SkillInstallState.Missing, false)]
        public void ShouldShowTemporarySkillNote_ReturnsExpectedValue(
            SkillInstallState installState,
            bool expected)
        {
            // Verifies the temporary skill note shows for every non-missing install state.
            bool shouldShow = ThirdPartyToolMigrationWizardStateRules.ShouldShowTemporarySkillNote(installState);

            Assert.That(shouldShow, Is.EqualTo(expected));
        }

        [TestCase(0, 1, 1, 4, 1000, 100, true)]
        [TestCase(10, 50, 1, 4, 1000, 100, false)]
        [TestCase(10, 110, 1, 4, 1000, 100, true)]
        [TestCase(10, 11, 4, 4, 1000, 100, true)]
        public void ShouldReportMigrationProgress_ReturnsExpectedValue(
            long lastReportTimestamp,
            long currentTimestamp,
            int processedItemCount,
            int totalItemCount,
            long stopwatchFrequency,
            int updateIntervalMilliseconds,
            bool expected)
        {
            // Verifies that migration progress updates are throttled while still reporting completion.
            ThirdPartyToolMigrationProgress progress =
                new(processedItemCount, totalItemCount);

            bool shouldReport = ThirdPartyToolMigrationWizardWindow.ShouldReportMigrationProgress(
                lastReportTimestamp,
                currentTimestamp,
                progress,
                stopwatchFrequency,
                updateIntervalMilliseconds);

            Assert.That(shouldReport, Is.EqualTo(expected));
        }

        [TestCase(false, true, true)]
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        public void ShouldApplyMigrationProgress_ReturnsExpectedValue(
            bool isCancellationRequested,
            bool hasActiveOperation,
            bool expected)
        {
            // Verifies that stale migration progress cannot overwrite a rendered preview result.
            bool shouldApply = ThirdPartyToolMigrationWizardWindow.ShouldApplyMigrationProgress(
                isCancellationRequested,
                hasActiveOperation);

            Assert.That(shouldApply, Is.EqualTo(expected));
        }

        [TestCase(1)]
        [TestCase(0)]
        public void ShouldRefreshAfterMigration_WhenMigrationCompletes_ReturnsFalse(int migratedFileCount)
        {
            // Verifies that a completed migration does not immediately start another scan.
            ThirdPartyToolMigrationResult result =
                new(migratedFileCount, migratedFileCount, System.Array.Empty<string>());

            bool shouldRefresh = ThirdPartyToolMigrationWizardWindow.ShouldRefreshAfterMigration(result);

            Assert.That(shouldRefresh, Is.False);
        }

        [TestCase(false, 0, true)]
        [TestCase(true, 0, false)]
        [TestCase(false, 1, true)]
        [TestCase(true, 1, true)]
        public void ShouldFinishMigrationOnMainThread_ReturnsExpectedValue(
            bool isCancellationRequested,
            int migratedFileCount,
            bool expected)
        {
            // Verifies that completed file writes still reach the main-thread asset refresh even after late cancellation.
            ThirdPartyToolMigrationResult result =
                new(migratedFileCount, migratedFileCount, System.Array.Empty<string>());

            bool shouldFinish = ThirdPartyToolMigrationWizardWindow.ShouldFinishMigrationOnMainThread(
                isCancellationRequested,
                result);

            Assert.That(shouldFinish, Is.EqualTo(expected));
        }

        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        public void ShouldRefreshAfterInterruptedMigration_ReturnsExpectedValue(
            bool isMigrationCompletionPending,
            bool isCancellationRequested,
            bool expected)
        {
            // Verifies that failed async migrations restore the wizard while user cancellations stay quiet.
            bool shouldRefresh = ThirdPartyToolMigrationWizardWindow.ShouldRefreshAfterInterruptedMigration(
                isMigrationCompletionPending,
                isCancellationRequested);

            Assert.That(shouldRefresh, Is.EqualTo(expected));
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
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 180f, frameSize, 1000f);

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
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 12f, frameSize, 1000f);

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
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 120f, frameSize, 1000f);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 148f)));
        }

        [Test]
        public void WithContentHeight_WhenMeasuredHeightExceedsMaxHeight_ClampsToMaxHeight()
        {
            // Verifies that content fitting never grows the migration wizard past the supplied maximum height.
            Rect initialRect = new(123f, 456f, 400f, 220f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 900f, frameSize, 500f);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 500f)));
        }

        [Test]
        public void WithContentHeight_WhenMaxHeightIsBelowMinimum_ClampsToMinimumHeight()
        {
            // Verifies that an unreasonably small maxHeight cannot shrink the window below the usable minimum.
            Rect initialRect = new(123f, 456f, 400f, 220f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect =
                ThirdPartyToolMigrationWizardWindow.WithContentHeight(initialRect, 900f, frameSize, 50f);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 120f)));
        }

        private static int GetVisualElementIndex(VisualElement root, VisualElement target)
        {
            Debug.Assert(root != null, "root must not be null");
            Debug.Assert(target != null, "target must not be null");

            System.Collections.Generic.List<VisualElement> elements = root.Query<VisualElement>().ToList();
            return elements.IndexOf(target);
        }
    }
}
