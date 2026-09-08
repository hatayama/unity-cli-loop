using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the post-domain-reload restore re-registers stored watch expressions:
    /// registration order, what happens to expressions that no longer compile, and that the
    /// store is left holding exactly what came back.
    /// </summary>
    [TestFixture]
    public sealed class WatchRestoreServiceTests
    {
        private const int MaxWaitFrames = 600;

        /// <summary>
        /// What: two stored watches are re-registered in the stored order and rewritten to the store.
        /// </summary>
        [UnityTest]
        public IEnumerator RestoreAsync_WithTwoCompilableRecords_RestoresThemInOrder()
        {
            WatchExpressionRegistry registry = CreateRegistry();
            InMemoryWatchPersistenceStore store = new(
                Record("first", "1 + 1", 5),
                Record("second", "2 + 2", 9));
            FakeWatchExpressionCompiler compiler = new();
            int monitorStartCount = 0;
            WatchRestoreService service = new(registry, compiler, store, () => monitorStartCount++);

            Task<WatchRestoreReport> task = service.RestoreAsync(CancellationToken.None);
            yield return WaitFor(task);

            WatchRestoreReport report = task.Result;
            Assert.That(report.RestoredCount, Is.EqualTo(2));
            Assert.That(report.Warnings, Is.Empty);
            Assert.That(monitorStartCount, Is.EqualTo(1));
            Assert.That(IdsOf(registry), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(IdsOf(store.Saved), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(store.Saved[0].MaxHistory, Is.EqualTo(5));
        }

        /// <summary>
        /// What: an expression that no longer compiles is reported and removed, the rest still restore.
        /// </summary>
        [UnityTest]
        public IEnumerator RestoreAsync_WhenOneExpressionNoLongerCompiles_ReportsItAndDropsItFromTheStore()
        {
            WatchExpressionRegistry registry = CreateRegistry();
            InMemoryWatchPersistenceStore store = new(
                Record("broken", "MissingType.Value", 20),
                Record("kept", "1 + 1", 20));
            FakeWatchExpressionCompiler compiler = new();
            compiler.FailFor("MissingType.Value", "The name 'MissingType' does not exist");
            WatchRestoreService service = new(registry, compiler, store, () => { });

            Task<WatchRestoreReport> task = service.RestoreAsync(CancellationToken.None);
            yield return WaitFor(task);

            WatchRestoreReport report = task.Result;
            Assert.That(report.RestoredCount, Is.EqualTo(1));
            Assert.That(report.Warnings, Has.Count.EqualTo(1));
            Assert.That(report.Warnings[0], Does.Contain("broken"));
            Assert.That(report.Warnings[0], Does.Contain("The name 'MissingType' does not exist"));
            Assert.That(IdsOf(registry), Is.EqualTo(new[] { "kept" }));
            Assert.That(
                IdsOf(store.Saved),
                Is.EqualTo(new[] { "kept" }),
                "A watch that cannot compile must not be retried on every later domain reload.");
        }

        /// <summary>
        /// What: a watch the user re-registered while restore was compiling keeps the user's registration.
        /// </summary>
        [UnityTest]
        public IEnumerator RestoreAsync_WhenTheIdWasRegisteredMeanwhile_KeepsTheExistingRegistrationSilently()
        {
            WatchExpressionRegistry registry = CreateRegistry();
            registry.Register("first", "999", new ConstantWatchExpressionEvaluator(999), 20);
            InMemoryWatchPersistenceStore store = new(Record("first", "1 + 1", 5));
            WatchRestoreService service = new(
                registry,
                new FakeWatchExpressionCompiler(),
                store,
                () => { });

            Task<WatchRestoreReport> task = service.RestoreAsync(CancellationToken.None);
            yield return WaitFor(task);

            WatchRestoreReport report = task.Result;
            Assert.That(report.RestoredCount, Is.EqualTo(0));
            Assert.That(report.Warnings, Is.Empty, "Losing a duplicate to the user's own registration is not a problem to report.");
            Assert.That(registry.GetEntries()[0].Expression, Is.EqualTo("999"));
            Assert.That(store.Saved[0].Expression, Is.EqualTo("999"));
        }

        /// <summary>
        /// What: an empty store compiles nothing and leaves the store untouched.
        /// </summary>
        [UnityTest]
        public IEnumerator RestoreAsync_WithEmptyStore_DoesNotCompileAnything()
        {
            WatchExpressionRegistry registry = CreateRegistry();
            InMemoryWatchPersistenceStore store = new();
            FakeWatchExpressionCompiler compiler = new();
            WatchRestoreService service = new(registry, compiler, store, () => { });

            Task<WatchRestoreReport> task = service.RestoreAsync(CancellationToken.None);
            yield return WaitFor(task);

            Assert.That(task.Result.RestoredCount, Is.EqualTo(0));
            Assert.That(compiler.CompileCount, Is.EqualTo(0));
            Assert.That(store.SaveCount, Is.EqualTo(0));
        }

        private static IEnumerator WaitFor(Task task)
        {
            int frames = 0;
            while (!task.IsCompleted && frames < MaxWaitFrames)
            {
                frames++;
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Restore did not complete within the frame budget.");
            Assert.That(task.Exception, Is.Null);
        }

        private static WatchExpressionRegistry CreateRegistry()
        {
            return new WatchExpressionRegistry(new FakeWatchEditorStateProvider());
        }

        private static WatchPersistedRecord Record(string id, string expression, int maxHistory)
        {
            return new WatchPersistedRecord { Id = id, Expression = expression, MaxHistory = maxHistory };
        }

        private static IReadOnlyList<string> IdsOf(WatchExpressionRegistry registry)
        {
            List<string> ids = new();
            foreach (WatchExpressionEntry entry in registry.GetEntries())
            {
                ids.Add(entry.Id);
            }

            return ids;
        }

        private static IReadOnlyList<string> IdsOf(IReadOnlyList<WatchPersistedRecord> records)
        {
            List<string> ids = new();
            foreach (WatchPersistedRecord record in records)
            {
                ids.Add(record.Id);
            }

            return ids;
        }

        private sealed class InMemoryWatchPersistenceStore : IWatchPersistenceStore
        {
            private IReadOnlyList<WatchPersistedRecord> _records;

            public InMemoryWatchPersistenceStore(params WatchPersistedRecord[] records)
            {
                _records = records;
            }

            public IReadOnlyList<WatchPersistedRecord> Saved => _records;
            public int SaveCount { get; private set; }

            public IReadOnlyList<WatchPersistedRecord> Load()
            {
                return _records;
            }

            public void Save(IReadOnlyList<WatchPersistedRecord> records)
            {
                SaveCount++;
                _records = records;
            }
        }

        private sealed class FakeWatchExpressionCompiler : IWatchExpressionCompiler
        {
            private readonly Dictionary<string, string> _failuresByExpression = new(StringComparer.Ordinal);

            public int CompileCount { get; private set; }

            public void FailFor(string expression, string errorMessage)
            {
                _failuresByExpression[expression] = errorMessage;
            }

            public Task<WatchCompilationResult> CompileAsync(string expression, CancellationToken ct)
            {
                CompileCount++;
                if (_failuresByExpression.TryGetValue(expression, out string errorMessage))
                {
                    return Task.FromResult(WatchCompilationResult.FailureResult(
                        "Watch expression compilation failed.",
                        new List<CompilationError> { new() { Line = 1, Column = 1, Message = errorMessage, ErrorCode = "CS0103" } }));
                }

                return Task.FromResult(
                    WatchCompilationResult.SuccessResult(new ConstantWatchExpressionEvaluator(1)));
            }
        }

        private sealed class ConstantWatchExpressionEvaluator : IWatchExpressionEvaluator
        {
            private readonly object _value;

            public ConstantWatchExpressionEvaluator(object value)
            {
                _value = value;
            }

            public WatchEvaluationResult Evaluate()
            {
                return WatchEvaluationResult.SuccessResult(_value);
            }
        }

        private sealed class FakeWatchEditorStateProvider : IWatchEditorStateProvider
        {
            public int FrameCount => 1;
            public bool IsPlaying => true;
            public bool IsPaused => true;
            public DateTime UtcNow => new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}
