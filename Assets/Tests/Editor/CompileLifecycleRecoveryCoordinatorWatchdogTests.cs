using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the compile recovery coordinator starts its watchdog for requests that end before the
    /// first poll, and how it aborts after a watchdog fault only for the request that is still current.
    /// </summary>
    public sealed class CompileLifecycleRecoveryCoordinatorWatchdogTests
    {
        private List<string> _abortReasons;
        private bool _isRequestCompleted;
        private int _editorCompilingQueries;
        private TaskCompletionSource<CompileResult> _currentCompileTask;

        [SetUp]
        public void SetUp()
        {
            _abortReasons = new List<string>();
            _isRequestCompleted = false;
            _editorCompilingQueries = 0;
            _currentCompileTask = null;
        }

        [Test]
        public void StartWatchdog_WithACanceledToken_AbortsAsCanceledBeforeStart()
        {
            // Verifies a request canceled before Unity started compiling is aborted with the before-start reason.
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator();

            coordinator.StartWatchdog(new TaskCompletionSource<CompileResult>(), new CancellationToken(true));

            Assert.That(
                _abortReasons,
                Is.EqualTo(new List<string> { "Compilation request was cancelled before it started." }));
            Assert.That(_editorCompilingQueries, Is.EqualTo(0));
        }

        [Test]
        public void StartWatchdog_WithACompletedRequest_StopsWithoutPolling()
        {
            // Verifies a request that already completed is neither polled nor aborted.
            _isRequestCompleted = true;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator();

            coordinator.StartWatchdog(new TaskCompletionSource<CompileResult>(), new CancellationToken(true));

            Assert.That(_abortReasons, Is.Empty);
            Assert.That(_editorCompilingQueries, Is.EqualTo(0));
        }

        [Test]
        public void AbortCompileAfterWatchdogFault_ForTheCurrentRequest_Aborts()
        {
            // Verifies a faulted watchdog aborts the request it was watching while that request is still current.
            TaskCompletionSource<CompileResult> compileTask = new TaskCompletionSource<CompileResult>();
            _currentCompileTask = compileTask;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator();

            coordinator.AbortCompileAfterWatchdogFault(compileTask);

            Assert.That(_abortReasons, Is.EqualTo(new List<string> { "Compilation watchdog failed unexpectedly." }));
        }

        [Test]
        public void AbortCompileAfterWatchdogFault_AfterANewerRequest_LeavesItAlone()
        {
            // Verifies a stale watchdog fault never aborts the newer request that replaced its own.
            _currentCompileTask = new TaskCompletionSource<CompileResult>();
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator();

            coordinator.AbortCompileAfterWatchdogFault(new TaskCompletionSource<CompileResult>());

            Assert.That(_abortReasons, Is.Empty);
        }

        [Test]
        public void AbortCompileAfterWatchdogFault_WithoutACurrentRequest_LeavesItAlone()
        {
            // Verifies a fault that arrives after the request finished aborts nothing.
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator();

            coordinator.AbortCompileAfterWatchdogFault(new TaskCompletionSource<CompileResult>());

            Assert.That(_abortReasons, Is.Empty);
        }

        private CompileLifecycleRecoveryCoordinator CreateCoordinator()
        {
            return new CompileLifecycleRecoveryCoordinator(
                isEditorCompiling: () =>
                {
                    _editorCompilingQueries++;
                    return false;
                },
                isRequestCompleted: () => _isRequestCompleted,
                getCurrentCompileTask: () => _currentCompileTask,
                findAssemblyDefinitionErrors: _ => throw new NotSupportedException(),
                getConsoleErrorEntries: () => throw new NotSupportedException(),
                getConsoleErrorCountAtCompileStart: () => throw new NotSupportedException(),
                validateNoDuplicateAsmdefNames: () => throw new NotSupportedException(),
                getIsForceCompile: () => throw new NotSupportedException(),
                getCompileMessages: () => throw new NotSupportedException(),
                getAssemblyFinishedCount: () => 0,
                getMonotonicSeconds: () => 0d,
                buildStateContext: context => context,
                abortWithResult: _ => throw new NotSupportedException(),
                abort: _abortReasons.Add);
        }
    }
}
