using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

using Stopwatch = System.Diagnostics.Stopwatch;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies when the shared-worker warm-up runs, what it skips, and what it hands the worker.
    /// </summary>
    [TestFixture]
    public sealed class SharedRoslynCompilerWorkerWarmUpTests
    {
        private const int WaitTimeoutMilliseconds = 30000;

        private const int EarlyCompletionWindowMilliseconds = 2000;

        /// <summary>Waits for a shared-worker warm-up that started before the run.</summary>
        [UnitySetUp]
        public IEnumerator WaitForSharedWorkerWarmUp()
        {
            Task warmUp = DynamicCodeServices.GetRegistry().GetSharedWorkerWarmUpTaskForTests();
            while (!warmUp.IsCompleted)
            {
                yield return null;
            }

            VibeLogger.ClearMemoryLogs();
        }

        [UnityTest]
        public IEnumerator Start_ReturnsBeforeAskingForReferences()
        {
            WarmUpParts parts = new WarmUpParts();
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            warmUp.Start();

            Assert.That(parts.CollectCalls, Is.EqualTo(0));
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());
            Assert.That(parts.CollectCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Start_AfterStopForTests_SkipsWithoutAskingForReferences()
        {
            WarmUpParts parts = new WarmUpParts();
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            yield return WaitUntilCompleted(warmUp.StopForTests());
            warmUp.Start();
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());

            Assert.That(parts.CollectCalls, Is.EqualTo(0));
            Assert.That(ReadSkipReason(), Is.EqualTo(SharedRoslynCompilerWorkerWarmUp.SkipReasonStoppedForTests));
        }

        [UnityTest]
        public IEnumerator Start_WithoutAReferenceProvider_SkipsAsNoHotReload()
        {
            WarmUpParts parts = new WarmUpParts();
            parts.Collector = null;
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            warmUp.Start();
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());

            Assert.That(parts.ResolveCalls, Is.EqualTo(0));
            Assert.That(parts.WarmCalls, Is.EqualTo(0));
            Assert.That(ReadSkipReason(), Is.EqualTo(SharedRoslynCompilerWorkerWarmUp.SkipReasonNoHotReload));
        }

        [UnityTest]
        public IEnumerator Start_WhenTheProviderListsNothing_SkipsWithoutResolvingCompilerPaths()
        {
            WarmUpParts parts = new WarmUpParts();
            parts.Collector = _ => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            warmUp.Start();
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());

            Assert.That(parts.ResolveCalls, Is.EqualTo(0));
            Assert.That(parts.WarmCalls, Is.EqualTo(0));
            Assert.That(ReadSkipReason(), Is.EqualTo(SharedRoslynCompilerWorkerWarmUp.SkipReasonNoTargets));
        }

        [UnityTest]
        public IEnumerator Start_WhenCompilerPathsAreMissing_SkipsWithoutWarmingOrLogging()
        {
            WarmUpParts parts = new WarmUpParts();
            parts.Paths = null;
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            warmUp.Start();
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());

            Assert.That(parts.WarmCalls, Is.EqualTo(0));
            Assert.That(ReadSkipReason(), Is.EqualTo(SharedRoslynCompilerWorkerWarmUp.SkipReasonCompilerUnavailable));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Start_WhenTheProviderThrows_LogsTheExceptionAndCompletes()
        {
            WarmUpParts parts = new WarmUpParts();
            parts.Collector = _ => throw new InvalidOperationException("provider failed for the test");
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("provider failed for the test"));

            warmUp.Start();
            Task task = warmUp.GetTaskForTests();
            yield return WaitUntilCompleted(task);

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            JObject complete = ReadSingleContext(SharedRoslynCompilerWorkerWarmUp.VibeLogWarmUpComplete);
            Assert.That((string)complete["outcome"], Is.EqualTo(SharedRoslynCompilerWorkerWarmUp.OutcomeFailed));
            Assert.That((string)complete["failureReason"], Is.EqualTo(nameof(InvalidOperationException)));
        }

        [UnityTest]
        public IEnumerator Start_HandsTheReferencesAndPathsToTheWorker()
        {
            string[] references = { "first.dll", "second.dll", "third.dll" };
            WarmUpParts parts = new WarmUpParts();
            parts.Collector = _ => Task.FromResult<IReadOnlyList<string>>(references);
            parts.Warm = () => Task.FromResult(SharedWorkerWarmUpOutcome.Answered(3));
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            warmUp.Start();
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());

            Assert.That(parts.WarmedReferences, Is.EqualTo(references));
            Assert.That(parts.WarmedPaths, Is.SameAs(parts.Paths));
            JObject complete = ReadSingleContext(SharedRoslynCompilerWorkerWarmUp.VibeLogWarmUpComplete);
            Assert.That((int)complete["referenceCount"], Is.EqualTo(references.Length));
            Assert.That((string)complete["outcome"], Is.EqualTo(SharedWorkerWarmUpOutcome.OutcomeAnswered));
            Assert.That((int)complete["errorCount"], Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies the package path is read on the main thread even when the reference list
        /// completes on a pool thread.
        /// </summary>
        [UnityTest]
        public IEnumerator Start_ReadsThePackagePathOnTheMainThread()
        {
            TaskCompletionSource<IReadOnlyList<string>> references = new TaskCompletionSource<IReadOnlyList<string>>();
            WarmUpParts parts = new WarmUpParts();
            parts.Collector = _ => references.Task;
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();
            try
            {
                warmUp.Start();
                yield return WaitUntil(() => parts.CollectCalls == 1);
                Task completion = Task.Run(() => references.SetResult(new[] { "first.dll" }));
                yield return WaitUntilCompleted(completion);
                yield return WaitUntilCompleted(warmUp.GetTaskForTests());

                Assert.That(parts.PackagePathReads, Is.EqualTo(1));
                Assert.That(parts.PackagePathReadOnMainThread, Is.True);
            }
            finally
            {
                references.TrySetResult(Array.Empty<string>());
            }
        }

        /// <summary>
        /// Verifies the compiler paths are resolved on a pool thread. The reference list completes
        /// synchronously, so everything outside a pool hand-off runs on the main thread.
        /// </summary>
        [UnityTest]
        public IEnumerator Start_ResolvesCompilerPathsOffTheMainThread()
        {
            WarmUpParts parts = new WarmUpParts();
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();

            warmUp.Start();
            yield return WaitUntilCompleted(warmUp.GetTaskForTests());

            Assert.That(parts.ResolveCalls, Is.EqualTo(1));
            Assert.That(parts.ResolvedOnMainThread, Is.False);
        }

        [UnityTest]
        public IEnumerator StopForTests_ReturnsTheWarmUpStillInFlight()
        {
            TaskCompletionSource<SharedWorkerWarmUpOutcome> worker = new TaskCompletionSource<SharedWorkerWarmUpOutcome>();
            WarmUpParts parts = new WarmUpParts();
            parts.Warm = () => worker.Task;
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();
            try
            {
                warmUp.Start();
                yield return WaitUntil(() => parts.WarmCalls == 1);

                Task stopped = warmUp.StopForTests();
                Assert.That(stopped.IsCompleted, Is.False);
                worker.SetResult(SharedWorkerWarmUpOutcome.AlreadyRunning());
                yield return WaitUntilCompleted(stopped);
            }
            finally
            {
                worker.TrySetResult(SharedWorkerWarmUpOutcome.AlreadyRunning());
            }
        }

        [UnityTest]
        public IEnumerator GetTaskForTests_CoversEveryStartedWarmUp()
        {
            TaskCompletionSource<SharedWorkerWarmUpOutcome> first = new TaskCompletionSource<SharedWorkerWarmUpOutcome>();
            TaskCompletionSource<SharedWorkerWarmUpOutcome> second = new TaskCompletionSource<SharedWorkerWarmUpOutcome>();
            ConcurrentQueue<TaskCompletionSource<SharedWorkerWarmUpOutcome>> workers =
                new ConcurrentQueue<TaskCompletionSource<SharedWorkerWarmUpOutcome>>(new[] { first, second });
            WarmUpParts parts = new WarmUpParts();
            parts.Warm = () =>
            {
                workers.TryDequeue(out TaskCompletionSource<SharedWorkerWarmUpOutcome> worker);
                return worker.Task;
            };
            SharedRoslynCompilerWorkerWarmUp warmUp = parts.Create();
            try
            {
                // Why wait between the starts: the first warm-up must be the one holding `first`.
                warmUp.Start();
                yield return WaitUntil(() => parts.WarmCalls == 1);
                warmUp.Start();
                yield return WaitUntil(() => parts.WarmCalls == 2);

                Task inFlight = warmUp.GetTaskForTests();
                // Why finish the later warm-up first: a task that tracked only the latest start
                // would then complete while the earlier one is still in flight.
                second.SetResult(SharedWorkerWarmUpOutcome.AlreadyRunning());
                yield return WaitAtMost(() => inFlight.IsCompleted, EarlyCompletionWindowMilliseconds);
                Assert.That(inFlight.IsCompleted, Is.False);
                first.SetResult(SharedWorkerWarmUpOutcome.AlreadyRunning());
                yield return WaitUntilCompleted(inFlight);
            }
            finally
            {
                first.TrySetResult(SharedWorkerWarmUpOutcome.AlreadyRunning());
                second.TrySetResult(SharedWorkerWarmUpOutcome.AlreadyRunning());
            }
        }

        [UnityTest]
        public IEnumerator ResetServerScopedServices_StartsAWarmUpWithoutRunningItInline()
        {
            WarmUpParts parts = new WarmUpParts();
            DynamicCodeServicesRegistry registry = new DynamicCodeServicesRegistry(parts.Create());
            try
            {
                registry.ResetServerScopedServices();

                Assert.That(parts.CollectCalls, Is.EqualTo(0));
                yield return WaitUntilCompleted(registry.GetSharedWorkerWarmUpTaskForTests());
                Assert.That(parts.CollectCalls, Is.EqualTo(1));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
            }
        }

        [UnityTest]
        public IEnumerator ResetServerScopedServicesBeforeDomainReload_StartsNoWarmUp()
        {
            WarmUpParts parts = new WarmUpParts();
            DynamicCodeServicesRegistry registry = new DynamicCodeServicesRegistry(parts.Create());
            try
            {
                registry.ResetServerScopedServicesBeforeDomainReload();
                for (int tick = 0; tick < 5; tick++)
                {
                    yield return null;
                }

                Assert.That(parts.CollectCalls, Is.EqualTo(0));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
            }
        }

        // Waits until the condition holds or the window ends, without failing either way.
        private static IEnumerator WaitAtMost(Func<bool> condition, int windowMilliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < windowMilliseconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntilCompleted(Task task)
        {
            return WaitUntil(() => task.IsCompleted);
        }

        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > WaitTimeoutMilliseconds)
                {
                    Assert.Fail("The condition did not hold within the timeout.");
                }

                yield return null;
            }
        }

        private static string ReadSkipReason()
        {
            return (string)ReadSingleContext(SharedRoslynCompilerWorkerWarmUp.VibeLogWarmUpSkipped)["reason"];
        }

        private static JObject ReadSingleContext(string operation)
        {
            JArray entries = JArray.Parse(VibeLogger.GetLogsForAi(operation));
            Assert.That(entries.Count, Is.EqualTo(1), "entries of " + operation);
            return (JObject)entries[0]["context"];
        }

        private static ExternalCompilerPaths CreateFakePaths()
        {
            return new ExternalCompilerPaths(
                "contents",
                "scripting",
                "dotnet",
                "csc.dll",
                "csc.runtimeconfig.json",
                "csc.deps.json",
                "Microsoft.CodeAnalysis.dll",
                "Microsoft.CodeAnalysis.CSharp.dll",
                "shared",
                ExternalCompilerLayoutKind.Unknown);
        }

        /// <summary>
        /// The three parts a warm-up is built from, faked: each records how it was called.
        /// </summary>
        private sealed class WarmUpParts
        {
            internal Func<CancellationToken, Task<IReadOnlyList<string>>> Collector =
                _ => Task.FromResult<IReadOnlyList<string>>(new[] { "first.dll" });

            internal ExternalCompilerPaths Paths = CreateFakePaths();

            internal Func<Task<SharedWorkerWarmUpOutcome>> Warm =
                () => Task.FromResult(SharedWorkerWarmUpOutcome.Answered(0));

            private int _collectCalls;
            private int _resolveCalls;
            private int _warmCalls;

            internal int CollectCalls => Volatile.Read(ref _collectCalls);

            internal int ResolveCalls => Volatile.Read(ref _resolveCalls);

            internal int WarmCalls => Volatile.Read(ref _warmCalls);

            internal bool ResolvedOnMainThread { get; private set; }

            internal int PackagePathReads { get; private set; }

            internal bool PackagePathReadOnMainThread { get; private set; }

            internal IReadOnlyList<string> WarmedReferences { get; private set; }

            internal ExternalCompilerPaths WarmedPaths { get; private set; }

            internal SharedRoslynCompilerWorkerWarmUp Create()
            {
                return new SharedRoslynCompilerWorkerWarmUp(
                    GetCollector,
                    ResolvePaths,
                    ReadPackagePath,
                    WarmWorker);
            }

            private Func<CancellationToken, Task<IReadOnlyList<string>>> GetCollector()
            {
                if (Collector == null)
                {
                    return null;
                }

                return ct =>
                {
                    Interlocked.Increment(ref _collectCalls);
                    return Collector(ct);
                };
            }

            private ExternalCompilerPaths ResolvePaths()
            {
                ResolvedOnMainThread = MainThreadSwitcher.IsMainThread;
                Interlocked.Increment(ref _resolveCalls);
                return Paths;
            }

            private string ReadPackagePath()
            {
                PackagePathReadOnMainThread = MainThreadSwitcher.IsMainThread;
                PackagePathReads++;
                return "package";
            }

            private Task<SharedWorkerWarmUpOutcome> WarmWorker(
                IReadOnlyList<string> references,
                ExternalCompilerPaths paths)
            {
                WarmedReferences = references;
                WarmedPaths = paths;
                Interlocked.Increment(ref _warmCalls);
                return Warm();
            }
        }
    }
}
