using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

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

        private static DynamicCodeExecutionScheduler CreateScheduler(
            DynamicCodeExecutionSchedulerHooks hooks = null,
            Action disposeResources = null)
        {
            return new DynamicCodeExecutionScheduler(
                disposeResources ?? (() => { }),
                hooks,
                busyHandoffWindowMilliseconds: 20,
                cancelledPrewarmHandoffWindowMilliseconds: 40);
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
