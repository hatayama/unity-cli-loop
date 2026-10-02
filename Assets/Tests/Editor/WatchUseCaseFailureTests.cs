using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the watch tool's rejection paths (validation, compilation, duplicate registration, and
    /// clearing an unknown watch) and the restore that runs after a domain reload, through test stores and
    /// compilers.
    /// </summary>
    public sealed class WatchUseCaseFailureTests
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
        /// Verifies an empty expression is rejected before compiling.
        /// </summary>
        [Test]
        public void EnableAsync_WithAnEmptyExpression_ReturnsValidationFailure()
        {
            UseUnreachableCompiler();

            WatchResponse response = Complete(WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = " ", MaxHistory = 5 },
                CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Expression must not be null or empty."));
        }

        /// <summary>
        /// Verifies a history size above the registry limit is rejected with the allowed range.
        /// </summary>
        [Test]
        public void EnableAsync_WithMaxHistoryAboveTheLimit_ReturnsValidationFailure()
        {
            UseUnreachableCompiler();

            WatchResponse response = Complete(WatchUseCase.EnableAsync(
                new EnableWatchSchema
                {
                    Id = "speed",
                    Expression = "1 + 2",
                    MaxHistory = WatchExpressionRegistry.MaxHistoryLimit + 1
                },
                CancellationToken.None));

            Assert.That(
                response.Message,
                Is.EqualTo($"MaxHistory must be between 1 and {WatchExpressionRegistry.MaxHistoryLimit}."));
        }

        /// <summary>
        /// Verifies a failed compile is returned with each compiler error and registers nothing.
        /// </summary>
        [Test]
        public void EnableAsync_WhenCompilationFails_ReturnsTheCompilerErrors()
        {
            CompilationError error = new CompilationError { Line = 1, Column = 5, Message = "bad token", ErrorCode = "CS1002" };
            WatchExpressionServices.OverrideCompilerForTesting(new StubWatchExpressionCompiler(
                WatchCompilationResult.FailureResult("Compilation failed.", new List<CompilationError> { error })));

            WatchResponse response = Complete(WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = "1 +", MaxHistory = 5 },
                CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Compilation failed."));
            Assert.That(response.CompilationErrors.Count, Is.EqualTo(1));
            Assert.That(response.CompilationErrors[0].Line, Is.EqualTo(1));
            Assert.That(response.CompilationErrors[0].Column, Is.EqualTo(5));
            Assert.That(response.CompilationErrors[0].ErrorCode, Is.EqualTo("CS1002"));
            Assert.That(WatchExpressionServices.Registry.GetEntries(), Is.Empty);
            Assert.That(_store.SaveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a second watch with the same id is rejected and the stored records stay untouched.
        /// </summary>
        [Test]
        public void EnableAsync_WithAnIdAlreadyRegistered_ReturnsTheRegistryFailure()
        {
            WatchExpressionServices.Registry.Register("speed", "1 + 2", new ConstantWatchExpressionEvaluator(3), 5);
            WatchExpressionServices.OverrideCompilerForTesting(new StubWatchExpressionCompiler(
                WatchCompilationResult.SuccessResult(new ConstantWatchExpressionEvaluator(4))));

            WatchResponse response = Complete(WatchUseCase.EnableAsync(
                new EnableWatchSchema { Id = "speed", Expression = "2 + 2", MaxHistory = 5 },
                CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Watch expression 'speed' is already registered."));
            Assert.That(WatchExpressionServices.Registry.GetEntries().Single().Expression, Is.EqualTo("1 + 2"));
            Assert.That(_store.SaveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies clearing needs either an id or All.
        /// </summary>
        [Test]
        public void Clear_WithoutAnIdOrAll_ReturnsValidationFailure()
        {
            WatchResponse response = WatchUseCase.Clear(new ClearWatchSchema { Id = " " });

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Id must not be null or empty unless All is true."));
        }

        /// <summary>
        /// Verifies clearing an unregistered watch fails without rewriting the stored records.
        /// </summary>
        [Test]
        public void Clear_WithAnUnknownId_ReportsItWasNotFound()
        {
            WatchResponse response = WatchUseCase.Clear(new ClearWatchSchema { Id = "missing" });

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Watch expression 'missing' was not found."));
            Assert.That(_store.SaveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an empty registry answers with an empty list and its own message.
        /// </summary>
        [Test]
        public void GetValues_WithoutWatches_ReportsThatNoneAreRegistered()
        {
            WatchResponse response = WatchUseCase.GetValues(new GetWatchValuesSchema());

            Assert.That(response.Watches, Is.Empty);
            Assert.That(response.Message, Is.EqualTo("No watch expressions are registered."));
            Assert.That(response.Warning, Is.Null);
        }

        /// <summary>
        /// Verifies a restore with nothing stored completes with the empty report and registers nothing.
        /// </summary>
        [Test]
        public void RestoreAfterDomainReload_WithAnEmptyStore_RecordsAnEmptyReport()
        {
            WatchExpressionServices.RestoreAfterDomainReload();

            Assert.That(WatchExpressionServices.LastRestoreReport, Is.SameAs(WatchRestoreReport.Empty));
            Assert.That(WatchExpressionServices.Registry.GetEntries(), Is.Empty);
        }

        private static void UseUnreachableCompiler()
        {
            // Keeps a broken validation away from the real dynamic-code compiler.
            WatchExpressionServices.OverrideCompilerForTesting(new StubWatchExpressionCompiler(
                WatchCompilationResult.FailureResult("compiler must not be reached", new List<CompilationError>())));
        }

        private static WatchResponse Complete(Task<WatchResponse> task)
        {
            // The stub compiler completes synchronously on the main thread, so the task is already done.
            Assert.That(task.IsCompletedSuccessfully, Is.True);
            return task.Result;
        }

        private sealed class InMemoryWatchPersistenceStore : IWatchPersistenceStore
        {
            private IReadOnlyList<WatchPersistedRecord> _records = Array.Empty<WatchPersistedRecord>();

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

        private sealed class StubWatchExpressionCompiler : IWatchExpressionCompiler
        {
            private readonly WatchCompilationResult _result;

            public StubWatchExpressionCompiler(WatchCompilationResult result)
            {
                _result = result;
            }

            public Task<WatchCompilationResult> CompileAsync(string expression, CancellationToken ct)
            {
                return Task.FromResult(_result);
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
