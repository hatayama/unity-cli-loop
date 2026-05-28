using System;
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
                _ => { });

            await watchdog.WatchAsync(CancellationToken.None);

            Assert.That(startTimeoutMs, Is.EqualTo(UnityCliLoopConstants.COMPILE_START_TIMEOUT_MS));
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
