using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests compile lifecycle recovery decisions without invoking Unity's real compiler.
    /// </summary>
    [TestFixture]
    public sealed class CompileLifecycleWatchdogTests
    {
        [Test]
        public async Task WatchAsync_WhenEditorStopsCompilingWithoutCallback_RequestsMissedCallbackRecovery()
        {
            // Verifies the watchdog recovers when Unity leaves compiling state without firing completion callback.
            SequenceCompilationState compilationState = new SequenceCompilationState(
                new bool[] { false, true, false, false, false, false, false });
            int missedCallbackStoppedMs = -1;
            int startTimeoutCount = 0;
            int cancellationCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => false,
                () => Task.CompletedTask,
                _ => { },
                _ => startTimeoutCount++,
                stoppedMs => missedCallbackStoppedMs = stoppedMs,
                _ => cancellationCount++,
                () => 0,
                () => 0d,
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(missedCallbackStoppedMs, Is.EqualTo(UnityCliLoopConstants.COMPILE_FINISH_MISSED_CALLBACK_GRACE_MS));
            Assert.That(startTimeoutCount, Is.EqualTo(0));
            Assert.That(cancellationCount, Is.EqualTo(0));
        }

        [Test]
        public async Task WatchAsync_WhenRequestCompletesAfterStart_StopsWithoutRecovery()
        {
            // Verifies the watchdog does not recover a request already completed by Unity's callback.
            SequenceCompilationState compilationState = new SequenceCompilationState(
                new bool[] { false, true, false, false, false });
            int waitCount = 0;
            int missedCallbackCount = 0;
            int startTimeoutCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 2,
                () =>
                {
                    waitCount++;
                    return Task.CompletedTask;
                },
                _ => { },
                _ => startTimeoutCount++,
                _ => missedCallbackCount++,
                _ => { },
                () => 0,
                () => 0d,
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(missedCallbackCount, Is.EqualTo(0));
            Assert.That(startTimeoutCount, Is.EqualTo(0));
        }

        [Test]
        public async Task WatchAsync_WhenRequestCompletesAfterOldFinishGrace_StopsWithoutRecovery()
        {
            // Verifies delayed finish callbacks are still accepted after the old 500ms grace window.
            SequenceCompilationState compilationState = new SequenceCompilationState(
                new bool[] { false, true, false, false, false, false, false, false, false });
            int waitCount = 0;
            int missedCallbackCount = 0;
            int startTimeoutCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 7,
                () =>
                {
                    waitCount++;
                    return Task.CompletedTask;
                },
                _ => { },
                _ => startTimeoutCount++,
                _ => missedCallbackCount++,
                _ => { },
                () => 0,
                () => 0d,
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(waitCount * UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS, Is.GreaterThan(500));
            Assert.That(missedCallbackCount, Is.EqualTo(0));
            Assert.That(startTimeoutCount, Is.EqualTo(0));
        }

        [Test]
        public async Task WatchAsync_WhenCompileNeverStarts_RequestsStartTimeout()
        {
            // Verifies the watchdog keeps the existing start-timeout recovery path.
            ConstantCompilationState compilationState = new ConstantCompilationState(false);
            int startTimeoutMs = -1;
            int missedCallbackCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => false,
                () => Task.CompletedTask,
                _ => { },
                waitedMs => startTimeoutMs = waitedMs,
                _ => missedCallbackCount++,
                _ => { },
                () => 0,
                () => 0d,
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(startTimeoutMs, Is.EqualTo(UnityCliLoopConstants.COMPILE_START_TIMEOUT_MS));
            Assert.That(missedCallbackCount, Is.EqualTo(0));
        }

        [Test]
        public void WatchAsync_WhenPollingFails_ExposesFaultForControllerRecovery()
        {
            // Verifies watchdog faults remain observable so the controller can abort the active compile request.
            ConstantCompilationState compilationState = new ConstantCompilationState(false);
            InvalidOperationException expectedException = new InvalidOperationException("poll failed");
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => false,
                () => Task.FromException(expectedException),
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => 0,
                () => 0d,
                _ => { });

            InvalidOperationException actualException = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await watchdog.WatchAsync(CancellationToken.None));

            Assert.That(actualException, Is.SameAs(expectedException));
        }

        [Test]
        public void IsCurrentCompileRequest_WhenTaskMatches_ReturnsTrue()
        {
            // Verifies watchdog fault recovery accepts the compile request it was created for.
            TaskCompletionSource<CompileResult> compileTask = new();

            bool isCurrentCompileRequest =
                CompileLifecycleRecoveryCoordinator.IsCurrentCompileRequest(compileTask, compileTask);

            Assert.That(isCurrentCompileRequest, Is.True);
        }

        [Test]
        public void IsCurrentCompileRequest_WhenTaskDiffers_ReturnsFalse()
        {
            // Verifies late watchdog faults from older requests cannot abort a newer compile request.
            TaskCompletionSource<CompileResult> currentCompileTask = new();
            TaskCompletionSource<CompileResult> staleCompileTask = new();

            bool isCurrentCompileRequest = CompileLifecycleRecoveryCoordinator.IsCurrentCompileRequest(
                currentCompileTask,
                staleCompileTask);

            Assert.That(isCurrentCompileRequest, Is.False);
        }

        [Test]
        public void CreateStoppedWithoutFinishResult_WhenAsmdefErrorsExist_ReturnsFailureWithAsmdefErrors()
        {
            // Verifies missed callback recovery reports actionable asmdef import errors instead of unknown status.
            const string asmdefPath = "Assets/Tests/EditMode/BlockKuzushi.EditMode.Tests.asmdef";
            AssemblyDefinitionConsoleError[] assemblyDefinitionErrors =
            {
                new(
                    $"Assembly has duplicate references: UnityEngine.TestRunner,UnityEditor.TestRunner ({asmdefPath})",
                    asmdefPath,
                    0)
            };
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionResult = new(assemblyDefinitionErrors);
            CompilerMessage[] compilerMessages = new CompilerMessage[0];

            CompileResult result = CompileResultFactory.CreateStoppedWithoutFinishResult(
                assemblyDefinitionResult,
                compilerMessages,
                false,
                "Compilation stopped before the finish callback.");

            Assert.That(result.Success, Is.False);
            Assert.That(result.IsIndeterminate, Is.False);
            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Errors[0].file, Is.EqualTo(asmdefPath));
            Assert.That(result.Errors[0].message, Does.Contain("duplicate references"));
        }

        [Test]
        public void CreateStoppedWithoutFinishResult_WhenNoAsmdefErrorsExist_ReturnsIndeterminateMessages()
        {
            // Verifies missed callback recovery keeps unknown status when no known importer error explains the gap.
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionResult =
                new(new AssemblyDefinitionConsoleError[0]);
            CompilerMessage[] compilerMessages =
            {
                new()
                {
                    type = CompilerMessageType.Error,
                    message = "CS0000: sample compile error",
                    file = "Assets/Scripts/Sample.cs",
                    line = 7
                }
            };

            CompileResult result = CompileResultFactory.CreateStoppedWithoutFinishResult(
                assemblyDefinitionResult,
                compilerMessages,
                false,
                "Compilation stopped before the finish callback.");

            Assert.That(result.Success, Is.Null);
            Assert.That(result.IsIndeterminate, Is.True);
            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Errors[0].message, Is.EqualTo("CS0000: sample compile error"));
        }

        /// <summary>
        /// Verifies a stalled assembly-finished counter while isCompiling stays true raises one warning for that episode.
        /// </summary>
        [Test]
        public async Task WatchAsync_WhenAssemblyProgressStallsWhileCompiling_WarnsOnce()
        {
            ConstantCompilationState compilationState = new ConstantCompilationState(true);
            int waitCount = 0;
            double clockSeconds = 0;
            int stallCallCount = 0;
            int stalledMs = -1;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 4,
                () =>
                {
                    waitCount++;
                    if (waitCount == 1)
                    {
                        clockSeconds = 300;
                    }

                    return Task.CompletedTask;
                },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => 1,
                () => clockSeconds,
                ms =>
                {
                    stallCallCount++;
                    stalledMs = ms;
                });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(stallCallCount, Is.EqualTo(1));
            Assert.That(stalledMs, Is.EqualTo(300000));
        }

        /// <summary>
        /// Verifies a zero assembly-finished count never raises the stall warning, even after the threshold.
        /// </summary>
        [Test]
        public async Task WatchAsync_WhenNoAssemblyHasFinished_DoesNotWarnOnElapsedTime()
        {
            ConstantCompilationState compilationState = new ConstantCompilationState(true);
            int waitCount = 0;
            double clockSeconds = 0;
            int stallCallCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 3,
                () =>
                {
                    waitCount++;
                    clockSeconds = 300;
                    return Task.CompletedTask;
                },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => 0,
                () => clockSeconds,
                _ => stallCallCount++);

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(stallCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a later assembly-finished increment restarts the stall clock instead of warning from the old anchor.
        /// </summary>
        [Test]
        public async Task WatchAsync_WhenAssemblyFinishedCountIncreases_ResetsStallMeasurement()
        {
            ConstantCompilationState compilationState = new ConstantCompilationState(true);
            int waitCount = 0;
            int assemblyFinishedCount = 1;
            double clockSeconds = 0;
            int stallCallCount = 0;
            int stalledMs = -1;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 6,
                () =>
                {
                    waitCount++;
                    if (waitCount == 1)
                    {
                        clockSeconds = 200;
                    }
                    else if (waitCount == 2)
                    {
                        assemblyFinishedCount = 2;
                    }
                    else if (waitCount == 3)
                    {
                        clockSeconds = 400;
                    }
                    else if (waitCount == 4)
                    {
                        clockSeconds = 500;
                    }

                    return Task.CompletedTask;
                },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => assemblyFinishedCount,
                () => clockSeconds,
                ms =>
                {
                    stallCallCount++;
                    stalledMs = ms;
                });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(stallCallCount, Is.EqualTo(1));
            Assert.That(stalledMs, Is.EqualTo(300000));
        }

        /// <summary>
        /// Verifies the stall warning does not end the watch, so a later compilationFinished can still complete it.
        /// </summary>
        [Test]
        public async Task WatchAsync_WhenAssemblyProgressStallWarned_ContinuesUntilRequestCompletes()
        {
            ConstantCompilationState compilationState = new ConstantCompilationState(true);
            int waitCount = 0;
            double clockSeconds = 0;
            int stallCallCount = 0;
            int missedCallbackCount = 0;
            int startTimeoutCount = 0;
            int cancellationCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 4,
                () =>
                {
                    waitCount++;
                    if (waitCount == 1)
                    {
                        clockSeconds = 300;
                    }

                    return Task.CompletedTask;
                },
                _ => { },
                _ => startTimeoutCount++,
                _ => missedCallbackCount++,
                _ => cancellationCount++,
                () => 1,
                () => clockSeconds,
                _ => stallCallCount++);

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(stallCallCount, Is.EqualTo(1));
            Assert.That(missedCallbackCount, Is.EqualTo(0));
            Assert.That(startTimeoutCount, Is.EqualTo(0));
            Assert.That(cancellationCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a threshold crossing does not warn when that poll sees isCompiling=false.
        /// </summary>
        [Test]
        public async Task WatchAsync_WhenElapsedExceedsThresholdButEditorIsNotCompiling_DoesNotWarn()
        {
            SequenceCompilationState compilationState = new SequenceCompilationState(
                new bool[] { true, false, false });
            int waitCount = 0;
            double clockSeconds = 0;
            int stallCallCount = 0;
            int missedCallbackCount = 0;
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => waitCount >= 2,
                () =>
                {
                    waitCount++;
                    if (waitCount == 1)
                    {
                        clockSeconds = 300;
                    }

                    return Task.CompletedTask;
                },
                _ => { },
                _ => { },
                _ => missedCallbackCount++,
                _ => { },
                () => 1,
                () => clockSeconds,
                _ => stallCallCount++);

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(stallCallCount, Is.EqualTo(0));
            Assert.That(missedCallbackCount, Is.EqualTo(0));
        }

        private sealed class SequenceCompilationState
        {
            private readonly bool[] _states;
            private int _index;

            public SequenceCompilationState(bool[] states)
            {
                _states = states ?? throw new ArgumentNullException(nameof(states));
            }

            public bool IsCompiling()
            {
                int index = Math.Min(_index, _states.Length - 1);
                _index++;
                return _states[index];
            }
        }

        private sealed class ConstantCompilationState
        {
            private readonly bool _isCompiling;

            public ConstantCompilationState(bool isCompiling)
            {
                _isCompiling = isCompiling;
            }

            public bool IsCompiling()
            {
                return _isCompiling;
            }
        }
    }
}
