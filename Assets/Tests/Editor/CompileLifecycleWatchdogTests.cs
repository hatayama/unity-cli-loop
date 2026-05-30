using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

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
                _ => { },
                _ => cancellationCount++);

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
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

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
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(startTimeoutMs, Is.EqualTo(UnityCliLoopConstants.COMPILE_START_TIMEOUT_MS));
            Assert.That(missedCallbackCount, Is.EqualTo(0));
        }

        [Test]
        public async Task WatchAsync_WhenCompileKeepsWaiting_ReportsDiagnosticSnapshots()
        {
            // Verifies long compile waits emit snapshots before timeout recovery.
            ConstantCompilationState compilationState = new ConstantCompilationState(false);
            List<CompileLifecycleWatchdogSnapshot> snapshots = new();
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                compilationState.IsCompiling,
                () => false,
                () => Task.CompletedTask,
                _ => { },
                _ => { },
                _ => { },
                snapshot => snapshots.Add(snapshot),
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(snapshots, Is.Not.Empty);
            Assert.That(
                snapshots[0].WaitedForStartMs,
                Is.EqualTo(UnityCliLoopConstants.COMPILE_WAIT_DIAGNOSTIC_LOG_INTERVAL_MS));
            Assert.That(snapshots[0].ObservedStart, Is.False);
            Assert.That(snapshots[0].EditorCompiling, Is.False);
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

            bool isCurrentCompileRequest = CompileController.IsCurrentCompileRequest(compileTask, compileTask);

            Assert.That(isCurrentCompileRequest, Is.True);
        }

        [Test]
        public void IsCurrentCompileRequest_WhenTaskDiffers_ReturnsFalse()
        {
            // Verifies late watchdog faults from older requests cannot abort a newer compile request.
            TaskCompletionSource<CompileResult> currentCompileTask = new();
            TaskCompletionSource<CompileResult> staleCompileTask = new();

            bool isCurrentCompileRequest = CompileController.IsCurrentCompileRequest(
                currentCompileTask,
                staleCompileTask);

            Assert.That(isCurrentCompileRequest, Is.False);
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
