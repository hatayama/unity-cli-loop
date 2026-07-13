using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    [TestFixture]
    public class DynamicCodeExecutionSchedulerTests
    {
        [Test]
        public async Task RunForegroundAsync_WhenExecutionIsAlreadyRunning_ShouldReturnBusyResult()
        {
            using DynamicCodeExecutionScheduler scheduler = CreateScheduler();
            TaskCompletionSource<bool> firstExecutionStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowFirstExecutionToComplete =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<string> firstExecutionTask = scheduler.RunForegroundAsync(
                async _ =>
                {
                    firstExecutionStarted.TrySetResult(true);
                    await allowFirstExecutionToComplete.Task;
                    return "first";
                },
                () => "busy",
                CancellationToken.None);

            await firstExecutionStarted.Task;

            string secondResult = await scheduler.RunForegroundAsync(
                _ => Task.FromResult("second"),
                () => "busy",
                CancellationToken.None);

            allowFirstExecutionToComplete.TrySetResult(true);
            string firstResult = await firstExecutionTask;

            Assert.That(secondResult, Is.EqualTo("busy"));
            Assert.That(firstResult, Is.EqualTo("first"));
        }

        [Test]
        public async Task RunForegroundAsync_WhenBackgroundPrewarmIsRunning_ShouldCancelItAndRunForegroundRequest()
        {
            using DynamicCodeExecutionScheduler scheduler = CreateScheduler();
            TaskCompletionSource<bool> backgroundStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<(bool Entered, string Result)> backgroundTask = scheduler.TryRunIfIdleAsync(
                true,
                async cancellationToken =>
                {
                    backgroundStarted.TrySetResult(true);
                    await WaitForCancellationAsync(cancellationToken);
                    return "background";
                },
                CancellationToken.None);

            await backgroundStarted.Task;

            string foregroundResult = await scheduler.RunForegroundAsync(
                _ => Task.FromResult("foreground"),
                () => "busy",
                CancellationToken.None);

            Assert.That(foregroundResult, Is.EqualTo("foreground"));
            Assert.That(async () => await backgroundTask, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task RunForegroundAsync_WhenForegroundArrivesAfterBackgroundStatePublished_ShouldPreemptPrewarm()
        {
            TaskCompletionSource<bool> backgroundStatePublished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowBackgroundToProceed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            DynamicCodeExecutionSchedulerHooks hooks = new()
            {
                AfterBackgroundExecutionStatePublishedAsync = async () =>
                {
                    backgroundStatePublished.TrySetResult(true);
                    await allowBackgroundToProceed.Task;
                }
            };

            using DynamicCodeExecutionScheduler scheduler = CreateScheduler(hooks);

            Task<(bool Entered, string Result)> backgroundTask = scheduler.TryRunIfIdleAsync(
                true,
                async cancellationToken =>
                {
                    await WaitForCancellationAsync(cancellationToken);
                    return "background";
                },
                CancellationToken.None);

            await backgroundStatePublished.Task;

            Task<string> foregroundTask = scheduler.RunForegroundAsync(
                _ => Task.FromResult("foreground"),
                () => "busy",
                CancellationToken.None);

            allowBackgroundToProceed.TrySetResult(true);

            string foregroundResult = await foregroundTask;

            Assert.That(foregroundResult, Is.EqualTo("foreground"));
            Assert.That(async () => await backgroundTask, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task RunForegroundAsync_WhenBackgroundPrewarmIgnoresCancellation_ShouldReturnBusyResult()
        {
            using DynamicCodeExecutionScheduler scheduler = CreateScheduler();
            TaskCompletionSource<bool> backgroundStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowBackgroundToComplete =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<(bool Entered, string Result)> backgroundTask = scheduler.TryRunIfIdleAsync(
                true,
                async _ =>
                {
                    backgroundStarted.TrySetResult(true);
                    await allowBackgroundToComplete.Task;
                    return "prewarm";
                },
                CancellationToken.None);

            await backgroundStarted.Task;

            string foregroundResult = await scheduler.RunForegroundAsync(
                _ => Task.FromResult("foreground"),
                () => "busy",
                CancellationToken.None);

            allowBackgroundToComplete.TrySetResult(true);
            (bool entered, string backgroundResult) = await backgroundTask;

            Assert.That(foregroundResult, Is.EqualTo("busy"));
            Assert.That(entered, Is.True);
            Assert.That(backgroundResult, Is.EqualTo("prewarm"));
        }

        [Test]
        public async Task RunForegroundAsync_WhenExecutionCompletesAfterBusyProbe_ShouldRetryBeforeReturningBusy()
        {
            TaskCompletionSource<bool> firstExecutionStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowFirstExecutionToComplete =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string> firstExecutionTask = null;

            DynamicCodeExecutionSchedulerHooks hooks = new()
            {
                AfterBusySemaphoreProbeFailedAsync = async () =>
                {
                    allowFirstExecutionToComplete.TrySetResult(true);
                    await firstExecutionTask;
                }
            };

            using DynamicCodeExecutionScheduler scheduler = CreateScheduler(hooks);
            firstExecutionTask = scheduler.RunForegroundAsync(
                async _ =>
                {
                    firstExecutionStarted.TrySetResult(true);
                    await allowFirstExecutionToComplete.Task;
                    return "first";
                },
                () => "busy",
                CancellationToken.None);

            await firstExecutionStarted.Task;

            string secondResult = await scheduler.RunForegroundAsync(
                _ => Task.FromResult("second"),
                () => "busy",
                CancellationToken.None);
            string firstResult = await firstExecutionTask;

            Assert.That(secondResult, Is.EqualTo("second"));
            Assert.That(firstResult, Is.EqualTo("first"));
        }

        [Test]
        public void RunForegroundAsync_WhenDisposedAfterSemaphoreAcquire_ShouldThrowObjectDisposedException()
        {
            int disposeCalls = 0;
            DynamicCodeExecutionScheduler scheduler = null;
            DynamicCodeExecutionSchedulerHooks hooks = new()
            {
                AfterSemaphoreEntered = () => scheduler.Dispose()
            };
            scheduler = CreateScheduler(hooks, () => disposeCalls++);

            try
            {
                Assert.That(
                    async () => await scheduler.RunForegroundAsync(
                        _ => Task.FromResult("foreground"),
                        () => "busy",
                        CancellationToken.None),
                    Throws.InstanceOf<ObjectDisposedException>());
                Assert.That(disposeCalls, Is.EqualTo(1));
            }
            finally
            {
                scheduler.Dispose();
            }
        }

        [Test]
        public async Task ShutdownAsync_WhenIdle_ShouldDisposeResourcesAndComplete()
        {
            // Verifies idle shutdown completes immediately and disposes resources once.
            int disposeCalls = 0;
            DynamicCodeExecutionScheduler scheduler = CreateScheduler(
                disposeResources: () => disposeCalls++);

            await scheduler.ShutdownAsync();

            Assert.That(disposeCalls, Is.EqualTo(1));
            Assert.That(scheduler.ShutdownAsync().IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task ShutdownAsync_WhenRunningActionObservesCancellation_ShouldDisposeBeforeTimeout()
        {
            // Verifies cooperative cancellation lets shutdown finish without hitting the timeout path.
            int disposeCalls = 0;
            System.Collections.Generic.List<string> warnings = new();
            DynamicCodeExecutionSchedulerHooks hooks = new()
            {
                LogWarning = message => warnings.Add(message)
            };
            DynamicCodeExecutionScheduler scheduler = CreateScheduler(
                hooks,
                () => disposeCalls++,
                shutdownTimeoutMilliseconds: 1000);

            TaskCompletionSource<bool> executionStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string> executionTask = scheduler.RunForegroundAsync(
                async cancellationToken =>
                {
                    executionStarted.TrySetResult(true);
                    await WaitForCancellationAsync(cancellationToken);
                    return "canceled";
                },
                () => "busy",
                CancellationToken.None);

            await executionStarted.Task;
            await scheduler.ShutdownAsync();

            Assert.That(async () => await executionTask, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(disposeCalls, Is.EqualTo(1));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task ShutdownAsync_WhenRunningActionIgnoresCancellation_ShouldCompleteAfterTimeoutAndDeferDispose()
        {
            // Verifies timeout unblocks shutdown while pool dispose waits for the late finally.
            int disposeCalls = 0;
            System.Collections.Generic.List<string> warnings = new();
            DynamicCodeExecutionSchedulerHooks hooks = new()
            {
                LogWarning = message => warnings.Add(message)
            };
            DynamicCodeExecutionScheduler scheduler = CreateScheduler(
                hooks,
                () => disposeCalls++,
                shutdownTimeoutMilliseconds: 40);

            TaskCompletionSource<bool> executionStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowExecutionToComplete =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string> executionTask = scheduler.RunForegroundAsync(
                async _ =>
                {
                    executionStarted.TrySetResult(true);
                    await allowExecutionToComplete.Task;
                    return "late";
                },
                () => "busy",
                CancellationToken.None);

            await executionStarted.Task;
            await scheduler.ShutdownAsync();

            Assert.That(disposeCalls, Is.EqualTo(0));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("timed out after 40ms"));
            Assert.That(warnings[0], Does.Contain("deferred until the running action reaches its finally"));

            allowExecutionToComplete.TrySetResult(true);
            string result = await executionTask;

            Assert.That(result, Is.EqualTo("late"));
            Assert.That(disposeCalls, Is.EqualTo(1));
        }

        private static DynamicCodeExecutionScheduler CreateScheduler(
            DynamicCodeExecutionSchedulerHooks hooks = null,
            Action disposeResources = null,
            int shutdownTimeoutMilliseconds = 1000)
        {
            return new DynamicCodeExecutionScheduler(
                disposeResources ?? (() => { }),
                hooks,
                busyHandoffWindowMilliseconds: 20,
                cancelledPrewarmHandoffWindowMilliseconds: 40,
                shutdownTimeoutMilliseconds: shutdownTimeoutMilliseconds);
        }

        private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => completionSource.TrySetCanceled(cancellationToken));
            await completionSource.Task;
        }
    }
}
