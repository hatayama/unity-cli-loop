using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies external watch tool input validation, empty-state responses, persistence of the
    /// registry across watch commands, and how a domain-reload restore report reaches the caller.
    /// </summary>
    [TestFixture]
    public sealed class WatchToolTests
    {
        private InMemoryWatchPersistenceStore _store;

        [SetUp]
        public void SetUp()
        {
            WatchExpressionServices.Registry.ClearAll();
            _store = new InMemoryWatchPersistenceStore();
            WatchExpressionServices.OverrideStoreForTesting(_store);
        }

        [TearDown]
        public void TearDown()
        {
            WatchExpressionServices.Registry.ClearAll();
            WatchExpressionServices.ResetForTesting();
        }

        /// <summary>
        /// Verifies an empty watch identifier is rejected without compiling user code.
        /// </summary>
        [Test]
        public void EnableAsync_WhenIdIsEmpty_ReturnsValidationFailure()
        {
            Task<WatchResponse> task = WatchUseCase.EnableAsync(
                new EnableWatchSchema { Expression = "1 + 2" },
                CancellationToken.None);

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Message, Does.Contain("Id must not be null or empty"));
        }

        /// <summary>
        /// Verifies a non-positive history limit is rejected as external input validation.
        /// </summary>
        [Test]
        public void EnableAsync_WhenMaxHistoryIsZero_ReturnsValidationFailure()
        {
            Task<WatchResponse> task = WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = "1 + 2", MaxHistory = 0 },
                CancellationToken.None);

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Message, Does.Contain("between 1 and 100"));
        }

        /// <summary>
        /// What: a rejected registration leaves the persisted records untouched.
        /// </summary>
        [Test]
        public void EnableAsync_WhenValidationFails_DoesNotTouchThePersistedRecords()
        {
            Task<WatchResponse> task = WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = string.Empty },
                CancellationToken.None);

            Assert.That(task.Result.Success, Is.False);
            Assert.That(_store.SaveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a successful enable-watch persists the watch, so the next domain reload restores it.
        /// </summary>
        [Test]
        public void EnableAsync_WhenRegistrationSucceeds_PersistsTheWatchForTheNextDomainReload()
        {
            WatchExpressionServices.OverrideCompilerForTesting(new StubWatchExpressionCompiler());

            Task<WatchResponse> task = WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = "1 + 2", MaxHistory = 9 },
                CancellationToken.None);

            Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(task.Result.Success, Is.True);
            Assert.That(_store.Records, Has.Count.EqualTo(1));
            Assert.That(_store.Records[0].Id, Is.EqualTo("speed"));
            Assert.That(_store.Records[0].Expression, Is.EqualTo("1 + 2"));
            Assert.That(_store.Records[0].MaxHistory, Is.EqualTo(9));
        }

        /// <summary>
        /// What: asking for exactly the watch the reload dropped explains why it is gone.
        /// </summary>
        [Test]
        public void GetValues_WhenTheRequestedWatchWasDroppedByTheRestore_ExplainsItInWarning()
        {
            WatchExpressionServices.SetLastRestoreReportForTesting(
                new WatchRestoreReport(0, new[] { "Watch 'speed' was not restored." }));

            WatchResponse response = WatchUseCase.GetValues(new GetWatchValuesSchema { Id = "speed" });

            Assert.That(response.Success, Is.False);
            Assert.That(response.Warning, Does.Contain("Watch 'speed' was not restored."));
        }

        /// <summary>
        /// What: the registry snapshot written for the next domain reload carries every restore input.
        /// </summary>
        [Test]
        public void SaveRegistrySnapshot_AfterRegistration_PersistsIdExpressionAndHistoryLimit()
        {
            WatchExpressionServices.Registry.Register(
                "speed",
                "1 + 2",
                new ConstantWatchExpressionEvaluator(3),
                7);

            WatchExpressionServices.SaveRegistrySnapshot();

            Assert.That(_store.Records, Has.Count.EqualTo(1));
            Assert.That(_store.Records[0].Id, Is.EqualTo("speed"));
            Assert.That(_store.Records[0].Expression, Is.EqualTo("1 + 2"));
            Assert.That(_store.Records[0].MaxHistory, Is.EqualTo(7));
        }

        /// <summary>
        /// What: clearing one watch removes it from the records the next domain reload would restore.
        /// </summary>
        [Test]
        public void Clear_WhenAWatchIsCleared_RemovesItFromThePersistedRecords()
        {
            WatchExpressionServices.Registry.Register(
                "speed",
                "1 + 2",
                new ConstantWatchExpressionEvaluator(3),
                20);
            WatchExpressionServices.SaveRegistrySnapshot();

            WatchResponse response = WatchUseCase.Clear(new ClearWatchSchema { Id = "speed" });

            Assert.That(response.Success, Is.True);
            Assert.That(_store.Records, Is.Empty);
        }

        /// <summary>
        /// What: clearing every watch empties the persisted records too.
        /// </summary>
        [Test]
        public void Clear_WithAll_EmptiesThePersistedRecords()
        {
            WatchExpressionServices.Registry.Register(
                "speed",
                "1 + 2",
                new ConstantWatchExpressionEvaluator(3),
                20);
            WatchExpressionServices.SaveRegistrySnapshot();

            WatchResponse response = WatchUseCase.Clear(new ClearWatchSchema { All = true });

            Assert.That(response.ClearedCount, Is.EqualTo(1));
            Assert.That(_store.Records, Is.Empty);
        }

        /// <summary>
        /// What: watches dropped by the last domain reload are reported on the next value read.
        /// </summary>
        [Test]
        public void GetValues_WhenTheLastRestoreDroppedWatches_ReportsThemInWarning()
        {
            WatchExpressionServices.SetLastRestoreReportForTesting(
                new WatchRestoreReport(0, new[] { "Watch 'speed' was not restored.", "Watch 'hp' was not restored." }));

            WatchResponse response = WatchUseCase.GetValues(new GetWatchValuesSchema());

            Assert.That(response.Warning, Does.Contain("Watch 'speed' was not restored."));
            Assert.That(response.Warning, Does.Contain("Watch 'hp' was not restored."));
        }

        /// <summary>
        /// What: with no restore report there is no Warning, and the field is omitted from the response JSON.
        /// </summary>
        [Test]
        public void GetValues_WhenNoRestoreHasRun_LeavesWarningUnset()
        {
            WatchResponse response = WatchUseCase.GetValues(new GetWatchValuesSchema());

            Assert.That(response.Warning, Is.Null);
            Assert.That(response.ShouldSerializeWarning(), Is.False);
        }

        /// <summary>
        /// Verifies get-watch-values returns a successful empty collection before registration.
        /// </summary>
        [Test]
        public void GetValues_WhenNoWatchesAreRegistered_ReturnsEmptySuccess()
        {
            WatchResponse response = WatchUseCase.GetValues(new GetWatchValuesSchema());

            Assert.That(response.Success, Is.True);
            Assert.That(response.Watches, Is.Empty);
            Assert.That(response.Message, Does.Contain("No watch expressions"));
        }

        private sealed class InMemoryWatchPersistenceStore : IWatchPersistenceStore
        {
            public IReadOnlyList<WatchPersistedRecord> Records { get; private set; } =
                Array.Empty<WatchPersistedRecord>();

            public int SaveCount { get; private set; }

            public IReadOnlyList<WatchPersistedRecord> Load()
            {
                return Records;
            }

            public void Save(IReadOnlyList<WatchPersistedRecord> records)
            {
                SaveCount++;
                Records = records;
            }
        }

        private sealed class StubWatchExpressionCompiler : IWatchExpressionCompiler
        {
            public Task<WatchCompilationResult> CompileAsync(string expression, CancellationToken ct)
            {
                return Task.FromResult(
                    WatchCompilationResult.SuccessResult(new ConstantWatchExpressionEvaluator(3)));
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
    }
}
