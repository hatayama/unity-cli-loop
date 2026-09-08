using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies what the re-arm pass replays after a domain reload and what it reports.
    /// </summary>
    [TestFixture]
    public sealed class PausePointRearmServiceTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointToolsFixture.cs";
        private const int FixtureLine = 11;

        private DateTime _nowUtc;
        private FakePauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _nowUtc = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
            _pauseController = new FakePauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => _nowUtc);
            PausePointPersistRequestLedger.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
            PausePointPersistRequestLedger.ResetForTests();
        }

        /// <summary>
        /// What: an --id record is re-armed, reported, published on the registry, and consumed
        /// from the store so it is not replayed again on a later reload.
        /// </summary>
        [Test]
        public void RearmAfterDomainReload_WithAnIdRecord_ReArmsAndReports()
        {
            InMemoryPausePointPersistenceStore store = new(RecordFor("jump"));
            PausePointRearmService service = new(store);

            IReadOnlyList<string> report = service.RearmAfterDomainReload();

            Assert.That(UloopPausePointRegistry.IsArmed("jump"), Is.True);
            Assert.That(report, Has.Count.EqualTo(1));
            Assert.That(report[0], Does.StartWith("Re-armed pause point 'jump'"));
            Assert.That(UloopPausePointRegistry.DomainReloadRearmReport, Is.EqualTo(report));
            Assert.That(store.Saved, Is.Empty, "The record must not be replayed again after the next reload.");
        }

        /// <summary>
        /// What: a source record is re-armed through File/Line and its report line names the
        /// resolved line and the source text at it.
        /// </summary>
        [Test]
        public void RearmAfterDomainReload_WithASourceRecord_ReportsTheResolvedLine()
        {
            InMemoryPausePointPersistenceStore store = new(SourceRecordFor(FixtureFilePath, FixtureLine));
            PausePointRearmService service = new(store);

            IReadOnlyList<string> report = service.RearmAfterDomainReload();

            Assert.That(report, Has.Count.EqualTo(1));
            Assert.That(report[0], Does.StartWith("Re-armed pause point"));
            Assert.That(report[0], Does.Contain($"{FixtureFilePath}:{FixtureLine}"));
            Assert.That(report[0], Does.Contain("int sum = left + right;"));
        }

        /// <summary>
        /// What: a record that no longer resolves is reported as a failure and warned about,
        /// and the records after it are still replayed.
        /// </summary>
        [Test]
        public void RearmAfterDomainReload_WhenOneRecordFails_WarnsAndKeepsGoing()
        {
            InMemoryPausePointPersistenceStore store = new(
                SourceRecordFor("Assets/Tests/Editor/NoSuchPausePointFixture.cs", 3),
                RecordFor("jump"));
            PausePointRearmService service = new(store);
            LogAssert.Expect(LogType.Warning, new RegexMatchAnything());

            IReadOnlyList<string> report = service.RearmAfterDomainReload();

            Assert.That(report, Has.Count.EqualTo(2));
            Assert.That(report[0], Does.StartWith("Could not re-arm pause point"));
            Assert.That(report[1], Does.StartWith("Re-armed pause point 'jump'"));
            Assert.That(UloopPausePointRegistry.IsArmed("jump"), Is.True);
        }

        /// <summary>
        /// What: a Release-code-optimization failure is reported in re-arm terms, not with the
        /// hand-issued enable message about an automatic Debug switch the re-arm never performs.
        /// </summary>
        [Test]
        public void RearmAfterDomainReload_WhenCodeOptimizationIsRelease_ExplainsTheReArmHasNoAutomaticSwitch()
        {
            InMemoryPausePointPersistenceStore store = new(
                SourceRecordFor("Assets/Tests/Editor/PausePointToolsFixture.cs", 12));
            PausePointRearmService service = new(
                store,
                _ => new PausePointResponse
                {
                    Success = false,
                    ErrorCode = SourcePausePointConstants.ErrorCodeReleaseCodeOptimization,
                    Message = SourcePausePointConstants.ReleaseCodeOptimizationRejectionMessage
                });
            LogAssert.Expect(LogType.Warning, new RegexMatchAnything());

            IReadOnlyList<string> report = service.RearmAfterDomainReload();

            Assert.That(report, Has.Count.EqualTo(1));
            Assert.That(
                report[0],
                Does.Contain("[" + SourcePausePointConstants.ErrorCodeReleaseCodeOptimization + "]"));
            Assert.That(
                report[0],
                Does.Contain("Code Optimization is Release and the re-arm does not switch it."));
            Assert.That(
                report[0],
                Does.Not.Contain("Automatic switch"),
                "The re-arm never runs the CLI's automatic Debug switch, so it must not claim it did.");
        }

        /// <summary>
        /// What: an empty store re-arms nothing and never calls the enable path.
        /// </summary>
        [Test]
        public void RearmAfterDomainReload_WithAnEmptyStore_EnablesNothing()
        {
            InMemoryPausePointPersistenceStore store = new();
            int enableCalls = 0;
            PausePointRearmService service = new(
                store,
                _ =>
                {
                    enableCalls++;
                    return new PausePointResponse();
                });

            IReadOnlyList<string> report = service.RearmAfterDomainReload();

            Assert.That(report, Is.Empty);
            Assert.That(enableCalls, Is.EqualTo(0));
        }

        private static PausePointPersistedRecord RecordFor(string id)
        {
            return PausePointPersistedRecord.FromSchema(
                id,
                new EnablePausePointSchema { Id = id, TimeoutSeconds = 30, Persist = true });
        }

        private static PausePointPersistedRecord SourceRecordFor(string file, int line)
        {
            return PausePointPersistedRecord.FromSchema(
                $"{file}:{line}",
                new EnablePausePointSchema { File = file, Line = line, TimeoutSeconds = 30, Persist = true });
        }

        // LogAssert needs a pattern; the exact failure text is asserted on the report instead.
        private sealed class RegexMatchAnything : System.Text.RegularExpressions.Regex
        {
            public RegexMatchAnything()
                : base(".*", System.Text.RegularExpressions.RegexOptions.Singleline)
            {
            }
        }

        /// <summary>
        /// Test double that records pause requests without mutating Unity Editor state.
        /// </summary>
        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused { get; private set; }

            public void Pause()
            {
                IsPaused = true;
            }

            public void Resume()
            {
                IsPaused = false;
            }
        }

        private sealed class InMemoryPausePointPersistenceStore : IPausePointPersistenceStore
        {
            private IReadOnlyList<PausePointPersistedRecord> _records;

            public InMemoryPausePointPersistenceStore(params PausePointPersistedRecord[] records)
            {
                _records = records;
            }

            public IReadOnlyList<PausePointPersistedRecord> Saved => _records;

            public IReadOnlyList<PausePointPersistedRecord> Load()
            {
                return _records;
            }

            public void Save(IReadOnlyList<PausePointPersistedRecord> records)
            {
                _records = records;
            }
        }
    }
}
