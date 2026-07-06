using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    // Guards the facade/service split introduced by the onion-architecture refactor.
    /// <summary>
    /// Test fixture that verifies Static Facade State Guard behavior.
    /// </summary>
    public sealed class StaticFacadeStateGuardTests
    {
        private static readonly string[] MigratedFacadePaths = new string[]
        {
            "Packages/src/Editor/Application/CliSetupApplicationService.cs",
            "Packages/src/Editor/Application/UnityCliLoopToolRegistrar.cs",
            "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/DynamicCodeServices.cs",
            "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/Execution/DynamicCodeForegroundWarmupState.cs",
            "Packages/src/Editor/ToolContracts/EditorFrameWaiter.cs",
            "Packages/src/Editor/FirstPartyTools/Common/InputRecording/InputRecorder.cs",
            "Packages/src/Editor/FirstPartyTools/RecordInput/Application/RecordingsApplicationFacade.cs",
            "Packages/src/Editor/FirstPartyTools/Common/InputRecording/InputReplayer.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateKeyboard/Application/KeyboardKeyState.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateMouseInput/Application/MouseInputState.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateMouseUi/Application/MouseDragState.cs",
            "Packages/src/Editor/FirstPartyTools/Common/Overlay/OverlayCanvasFactory.cs",
            "Packages/src/Editor/ToolContracts/MainThreadSwitcher.cs",
            "Packages/src/Editor/ToolContracts/VibeLogger.cs",
            "Packages/src/Editor/Application/UnityCliLoopServerApplicationService.cs",
            "Packages/src/Editor/Domain/UnityCliLoopSessionFlagsFacade.cs",
            "Packages/src/Editor/Domain/UnityCliLoopCompileResultSessionRepositoryFacade.cs",
            "Packages/src/Editor/Domain/UnityCliLoopPendingCompileSessionRepositoryFacade.cs",
            "Packages/src/Editor/Domain/UnityCliLoopCompileSessionLifecycleFacade.cs",
            "Packages/src/Runtime/RecordInput/RecordInputOverlayState.cs",
            "Packages/src/Runtime/ReplayInput/ReplayInputOverlayState.cs",
            "Packages/src/Runtime/SimulateKeyboard/SimulateKeyboardOverlayState.cs",
            "Packages/src/Runtime/SimulateMouseInput/SimulateMouseInputOverlayState.cs",
            "Packages/src/Runtime/SimulateMouseUi/SimulateMouseUiOverlayState.cs"
        };

        private static readonly string[] PublicContractPaths = new string[]
        {
            "Packages/src/Editor/ToolContracts/UnityCliLoopToolResponse.cs"
        };

        private static readonly string[] InstanceServicePaths = new string[]
        {
            "Packages/src/Editor/Application/SessionRecoveryService.cs",
            "Packages/src/Editor/Application/UseCases/SkillSetupUseCase.cs",
            "Packages/src/Editor/Domain/UnityCliLoopCompileSessionLifecycleService.cs",
            "Packages/src/Editor/Infrastructure/Server/DomainReloadDetectionFileService.cs"
        };

        private static readonly Regex DirectMutableStaticFieldPattern = new Regex(
            @"\b(private|internal|public|protected)\s+static\s+(?!readonly\b)(?!event\b)(?!extern\b)[^(\r\n;=]*[;=]",
            RegexOptions.Compiled);

        private static readonly Regex ReadonlyMutableStaticFieldPattern = new Regex(
            @"\b(private|internal|public|protected)\s+static\s+readonly\s+([^;=]+)",
            RegexOptions.Compiled);

        private static readonly Regex DirectStaticEventPattern = new Regex(
            @"\b(private|internal|public|protected)\s+static\s+event\b",
            RegexOptions.Compiled);

        private static readonly Regex StaticClassPattern = new Regex(
            @"\b(public|internal|private|protected)\s+static\s+class\b",
            RegexOptions.Compiled);

        private static readonly Regex DiscardedMainThreadTimerTaskPattern = new Regex(
            @"_\s*=\s*TimerDelay\.WaitThenExecuteOnMainThread",
            RegexOptions.Compiled);

        private static readonly Regex AllowedStaticIdentifierPattern = new Regex(
            @"\b(ServiceValue|RepositoryValue|RegistryValue|RegisteredUseCase)\b",
            RegexOptions.Compiled);

        [Test]
        public void MigratedFacadeFiles_WhenScanned_DoNotOwnMutableStaticState()
        {
            // Tests that migrated facades keep state inside instance services instead of direct static fields.
            List<string> violations = FindMutableStaticFieldViolations();

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void ProductionSources_WhenScanned_DoNotDeclareStaticEvents()
        {
            // Tests that static entrypoints do not hide event subscription lifetimes.
            List<string> violations = FindDirectStaticEventViolations();

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void PublicContracts_WhenScanned_DoNotOwnMutableStaticState()
        {
            // Tests that extension-facing contracts do not share mutable state across tool responses.
            List<string> violations = FindMutableStaticFieldViolations(PublicContractPaths);

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void InstanceServices_WhenScanned_AreNotStaticClasses()
        {
            // Tests that migrated services stay instance-owned instead of sliding back into static services.
            List<string> violations = FindStaticClassViolations(InstanceServicePaths);

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void MutableStaticFieldLine_WhenInitializerUsesTargetTypedNew_IsReported()
        {
            // Tests that the guard catches mutable static fields even when the initializer contains new().
            string line = "private static List<int> Cache = new();";
            bool isViolation =
                !IsAllowedStaticLine(line)
                && (DirectMutableStaticFieldPattern.IsMatch(line)
                    || ReadonlyMutableStaticFieldPattern.IsMatch(line));

            Assert.That(isViolation, Is.True);
        }

        [Test]
        public void MutableStaticFieldLine_WhenIdentifierOnlyContainsAllowedName_IsReported()
        {
            // Tests that the allowlist only accepts exact static facade backing-field identifiers.
            string line = "private static int RegisteredUseCaseCount = 0;";
            bool isViolation =
                !IsAllowedStaticLine(line)
                && DirectMutableStaticFieldPattern.IsMatch(line);

            Assert.That(isViolation, Is.True);
        }

        [Test]
        public void RecordingsApplicationFacade_WhenStartingDelayedRecording_DoesNotDiscardTimerTask()
        {
            // Tests that delayed recording observes timeout faults and keeps its countdown cleanup path reachable.
            string source = ReadSourceFile(
                "Packages/src/Editor/FirstPartyTools/RecordInput/Application/RecordingsApplicationFacade.cs");

            Assert.That(DiscardedMainThreadTimerTaskPattern.IsMatch(source), Is.False);
            Assert.That(source, Does.Contain("QueueCountdownCleanup"));
            Assert.That(source, Does.Contain("StartDelayedRecordingAsync(delayMilliseconds, generation, CancellationToken.None)"));
            Assert.That(source, Does.Contain("StartDelayedRecordingAsync(int delayMilliseconds, int generation, CancellationToken ct)"));
        }

        [Test]
        public void RecordInputDelayedStarts_WhenCallbackIsStale_DoNotClearCurrentCountdown()
        {
            // Tests that stale delayed-recording callbacks cannot clear a newer countdown instance.
            string facadeSource = ReadSourceFile(
                "Packages/src/Editor/FirstPartyTools/RecordInput/Application/RecordingsApplicationFacade.cs");
            string useCaseSource = ReadSourceFile(
                "Packages/src/Editor/FirstPartyTools/RecordInput/RecordInputUseCase.cs");

            Assert.That(facadeSource, Does.Contain("if (generation != _countdownGeneration)\n            {\n                return;\n            }"));
            Assert.That(useCaseSource, Does.Contain("int generation = Interlocked.Increment(ref _delayedStartGeneration);"));
            Assert.That(useCaseSource, Does.Contain("QueueCountdownCleanup(generation);"));
            Assert.That(useCaseSource, Does.Contain("if (!IsCurrentDelayedStartGeneration(generation))"));
        }

        [Test]
        public void TimerDelay_WhenMainThreadActionCompletes_CancelsTimeoutWait()
        {
            // Tests that successful posted actions do not leave the timeout timer running until expiry.
            string source = ReadSourceFile("Packages/src/Editor/ToolContracts/TimerDelay.cs");

            Assert.That(source, Does.Contain("CancellationTokenSource.CreateLinkedTokenSource(ct)"));
            Assert.That(source, Does.Contain("timeoutCancellationSource.Cancel();"));
        }

        [Test]
        public void EditorFrameWaiterManualTests_WhenWaitTimesOut_ReportFailureBeforeCompletionLogs()
        {
            // Tests that manual frame-wait checks surface timeout failures instead of silently continuing.
            string source = ReadSourceFile("Assets/Editor/EditorDelayManualTests.cs");

            Assert.That(source, Does.Contain("private static async Task WaitFramesForManualTestAsync"));
            Assert.That(source, Does.Contain("Debug.LogWarning($\"[WaitFramesForManualTestAsync] Timed out after"));
            Assert.That(source, Does.Contain("throw new TimeoutException("));
        }

        [Test]
        public void ScreenshotUseCase_WhenDestroyingTimedOutAnnotationOverlay_UsesReferenceNullCheck()
        {
            // Tests that screenshot timeout cleanup does not call UnityEngine.Object null operators off-thread.
            string source = ReadSourceFile("Packages/src/Editor/FirstPartyTools/Screenshot/ScreenshotUseCase.cs");

            Assert.That(source, Does.Contain("ReferenceEquals(annotationOverlay, null)"));
            Assert.That(source, Does.Not.Contain("annotationOverlay == null"));
        }

        [Test]
        public void ScreenshotUseCase_WhenWindowCaptureTimesOut_PreservesPartialScreenshots()
        {
            // Tests that window capture timeout results keep screenshots already written to disk.
            string source = ReadSourceFile("Packages/src/Editor/FirstPartyTools/Screenshot/ScreenshotUseCase.cs");

            Assert.That(source, Does.Contain("return CreateTimedOutResult(\"EditorWindow capture\", correlationId, screenshots);"));
            Assert.That(source, Does.Contain("Screenshots = screenshots,"));
        }

        [Test]
        public void DynamicCodeDomainReloadWaitSignal_WhenCheckingCompileState_SwitchesToMainThreadFirst()
        {
            // Tests that timeout continuations do not read UnityEditor compile state off the editor thread.
            string source = ReadSourceFile(
                "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/DynamicCodeDomainReloadWaitSignal.cs");
            int methodIndex = source.IndexOf(
                "public async Task<bool> ShouldWaitAsync(CancellationToken ct)",
                System.StringComparison.Ordinal);
            int mainThreadSwitchIndex = source.IndexOf(
                "await MainThreadSwitcher.SwitchToMainThread(ct);",
                methodIndex,
                System.StringComparison.Ordinal);
            int editorCompilingIndex = source.IndexOf(
                "EditorApplication.isCompiling",
                methodIndex,
                System.StringComparison.Ordinal);

            Assert.That(methodIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(mainThreadSwitchIndex, Is.GreaterThan(methodIndex));
            Assert.That(editorCompilingIndex, Is.GreaterThan(mainThreadSwitchIndex));
        }

        [Test]
        public void ExecuteDynamicCodeUseCase_WhenAwaitingWorkflow_DoesNotCaptureEditorSynchronizationContext()
        {
            // Tests that timeout-sensitive dynamic-code awaits do not capture Unity's synchronization context.
            string source = ReadSourceFile(
                "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/ExecuteDynamicCodeUseCase.cs");

            Assert.That(source, Does.Contain("await WarmForegroundExecutionPathIfNeededAsync(parameters, cancellationToken)\n                    .ConfigureAwait(false);"));
            Assert.That(source, Does.Contain("await ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false);"));
            Assert.That(source, Does.Contain("await RetryMissingReturnIfNeeded(\n                    executionResult,"));
            Assert.That(source, Does.Contain("cancellationToken).ConfigureAwait(false);"));
            Assert.That(source, Does.Contain("await _runtime.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);"));
            Assert.That(source, Does.Contain("await _runtime.TryExecuteIfIdleAsync(\n                request,\n                cancellationToken).ConfigureAwait(false);"));
        }

        [Test]
        public void SimulateMouseUiUseCase_WhenReturningAfterTimeout_UsesCapturedUnityObjectState()
        {
            // Tests that timeout result branches use plain captured bools instead of UnityEngine.Object null checks.
            string source = ReadSourceFile("Packages/src/Editor/FirstPartyTools/SimulateMouseUi/SimulateMouseUiUseCase.cs");

            Assert.That(source, Does.Contain("bool hitTarget = resolvedTargets.Target != null;"));
            Assert.That(source, Does.Contain("bool shouldReleasePointer = resolvedTargets.RawTarget != null && resolvedTargets.Target != null;"));
            Assert.That(source, Does.Not.Contain("CreateClickResult(parameters, inputPos, targetName, resolvedTargets.Target != null)"));
            Assert.That(source, Does.Not.Contain("CreateLongPressResult(parameters, inputPos, targetName, resolvedTargets.Target != null)"));
        }

        [Test]
        public void VibeLogger_WhenCollectingEnvironmentInfo_GatesUnityEditorApiBehindMainThreadCheck()
        {
            // Tests that debug logging from timeout continuations does not read UnityEditor APIs off-thread.
            string source = ReadSourceFile("Packages/src/Editor/ToolContracts/VibeLogger.cs");
            int methodIndex = source.IndexOf("private EnvironmentInfo GetEnvironmentInfo()", System.StringComparison.Ordinal);
            int mainThreadCheckIndex = source.IndexOf("if (!IsEditorMainThread)", methodIndex, System.StringComparison.Ordinal);
            int editorApplicationIndex = source.IndexOf("EditorApplication.isCompiling", methodIndex, System.StringComparison.Ordinal);

            Assert.That(methodIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(mainThreadCheckIndex, Is.GreaterThan(methodIndex));
            Assert.That(editorApplicationIndex, Is.GreaterThan(mainThreadCheckIndex));
            Assert.That(source, Does.Contain("DOMAIN_RELOAD_STATE_UNAVAILABLE_OFF_MAIN_THREAD"));
            Assert.That(source, Does.Not.Contain("UnityEngine.Application.dataPath"));
            Assert.That(source, Does.Not.Contain("Path.Combine(_logDirectory"));
        }

        private static List<string> FindMutableStaticFieldViolations()
        {
            return FindMutableStaticFieldViolations(MigratedFacadePaths);
        }

        private static string ReadSourceFile(string relativePath)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string absolutePath = Path.Combine(projectRoot, relativePath);
            return File.ReadAllText(absolutePath);
        }

        private static List<string> FindMutableStaticFieldViolations(string[] relativePaths)
        {
            List<string> violations = new();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();

            for (int pathIndex = 0; pathIndex < relativePaths.Length; pathIndex++)
            {
                string relativePath = relativePaths[pathIndex];
                string absolutePath = Path.Combine(projectRoot, relativePath);
                string[] lines = File.ReadAllLines(absolutePath);

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (IsAllowedStaticLine(line))
                    {
                        continue;
                    }

                    if (DirectMutableStaticFieldPattern.IsMatch(line)
                        || ReadonlyMutableStaticFieldPattern.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineIndex + 1}: {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        private static List<string> FindDirectStaticEventViolations()
        {
            List<string> violations = new();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string packagesSrcRoot = Path.Combine(projectRoot, "Packages/src");
            string[] sourcePaths = Directory.GetFiles(packagesSrcRoot, "*.cs", SearchOption.AllDirectories);

            for (int pathIndex = 0; pathIndex < sourcePaths.Length; pathIndex++)
            {
                string absolutePath = sourcePaths[pathIndex];
                string relativePath = Path.GetRelativePath(projectRoot, absolutePath);
                string[] lines = File.ReadAllLines(absolutePath);

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (DirectStaticEventPattern.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineIndex + 1}: {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        private static List<string> FindStaticClassViolations(string[] relativePaths)
        {
            List<string> violations = new();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();

            for (int pathIndex = 0; pathIndex < relativePaths.Length; pathIndex++)
            {
                string relativePath = relativePaths[pathIndex];
                string absolutePath = Path.Combine(projectRoot, relativePath);
                string[] lines = File.ReadAllLines(absolutePath);

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (StaticClassPattern.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineIndex + 1}: {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        private static bool IsAllowedStaticLine(string line)
        {
            if (line.Contains("=>"))
            {
                return true;
            }

            if (line.Contains("(") && !line.Contains("=") && !line.TrimEnd().EndsWith(";"))
            {
                return true;
            }

            return AllowedStaticIdentifierPattern.IsMatch(line);
        }
    }
}
