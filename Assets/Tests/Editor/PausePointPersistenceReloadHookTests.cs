using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which persisted enable requests reach the store just before a domain reload.
    /// </summary>
    [TestFixture]
    public sealed class PausePointPersistenceReloadHookTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointToolsFixture.cs";
        private const int FixtureLine = 12;

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
        /// What: only the pause points still armed at reload time are written out.
        /// </summary>
        [Test]
        public void SnapshotNow_SavesOnlyTheArmedRecords()
        {
            PausePointPersistRequestLedger.Upsert(RecordFor("armed"));
            PausePointPersistRequestLedger.Upsert(RecordFor("never-enabled"));
            UloopPausePointRegistry.Enable("armed", 30);
            InMemoryPausePointPersistenceStore store = new();

            PausePointPersistenceReloadHook.SnapshotNow(store);

            Assert.That(RegistryIdsOf(store.Saved), Is.EqualTo(new[] { "armed" }));
        }

        /// <summary>
        /// What: a pause point whose capture window ran out without any status poll is expired at
        /// snapshot time instead of being replayed with a fresh full timeout.
        /// </summary>
        [Test]
        public void SnapshotNow_SavesNothingForAPausePointWhoseTimeoutElapsedUnobserved()
        {
            PausePointPersistRequestLedger.Upsert(RecordFor("jump"));
            UloopPausePointRegistry.Enable("jump", 30);
            _nowUtc = _nowUtc.AddSeconds(31);
            InMemoryPausePointPersistenceStore store = new();

            PausePointPersistenceReloadHook.SnapshotNow(store);

            Assert.That(store.Saved, Is.Empty);
            Assert.That(
                UloopPausePointRegistry.GetStatus("jump").Status,
                Is.EqualTo(UloopPausePointStatus.Expired));
        }

        /// <summary>
        /// What: Initialize subscribes to the before-reload event, so firing the handler it
        /// registered writes the armed persisted records out.
        /// </summary>
        [Test]
        public void Initialize_SubscribesAHandlerThatSnapshotsTheArmedRecords()
        {
            PausePointPersistRequestLedger.Upsert(RecordFor("armed"));
            UloopPausePointRegistry.Enable("armed", 30);
            InMemoryPausePointPersistenceStore store = new();
            AssemblyReloadEvents.AssemblyReloadCallback subscribed = null;

            PausePointPersistenceReloadHook.Initialize(store, handler => subscribed = handler);

            Assert.That(subscribed, Is.Not.Null, "Initialize must subscribe to the before-reload event.");
            Assert.That(store.Saved, Is.Empty, "Nothing is written until the reload actually starts.");
            subscribed();
            Assert.That(RegistryIdsOf(store.Saved), Is.EqualTo(new[] { "armed" }));
        }

        /// <summary>
        /// What: a pause point the user cleared before the reload does not come back.
        /// </summary>
        [Test]
        public void SnapshotNow_SavesNothingForAClearedPausePoint()
        {
            PausePointPersistRequestLedger.Upsert(RecordFor("jump"));
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Clear("jump");
            InMemoryPausePointPersistenceStore store = new();

            PausePointPersistenceReloadHook.SnapshotNow(store);

            Assert.That(store.Saved, Is.Empty);
        }

        /// <summary>
        /// What: enabling without --persist records nothing to replay.
        /// </summary>
        [Test]
        public async Task Enable_WithoutPersist_LeavesTheLedgerEmpty()
        {
            await EnableByIdAsync("jump", false);
            InMemoryPausePointPersistenceStore store = new();

            PausePointPersistenceReloadHook.SnapshotNow(store);

            Assert.That(store.Saved, Is.Empty);
        }

        /// <summary>
        /// What: re-enabling a persisted pause point without --persist takes it back off
        /// persistence, so it is no longer replayed after the reload.
        /// </summary>
        [Test]
        public async Task Enable_WithoutPersistAfterAPersistedEnable_DropsTheRecord()
        {
            await EnableByIdAsync("jump", true);
            await EnableByIdAsync("jump", false);
            InMemoryPausePointPersistenceStore store = new();

            PausePointPersistenceReloadHook.SnapshotNow(store);

            Assert.That(store.Saved, Is.Empty);
        }

        /// <summary>
        /// What: a source pause point is keyed by the registry's file:line id, and its record
        /// carries the File/Line request rather than an Id, so it can be replayed.
        /// </summary>
        [Test]
        public async Task Enable_BySourceLocationWithPersist_RecordsTheResolvedRegistryId()
        {
            PausePointResponse response = await EnableByFileLineAsync(FixtureFilePath, FixtureLine, true);
            InMemoryPausePointPersistenceStore store = new();

            PausePointPersistenceReloadHook.SnapshotNow(store);

            Assert.That(RegistryIdsOf(store.Saved), Is.EqualTo(new[] { response.Id }));
            PausePointPersistedRecord saved = store.Saved[0];
            Assert.That(saved.Id, Is.Empty, "A source pause point replays through File/Line, never through an Id.");
            Assert.That(saved.File, Is.EqualTo(FixtureFilePath));
            Assert.That(saved.Line, Is.EqualTo(FixtureLine));
        }

        private static PausePointPersistedRecord RecordFor(string registryId)
        {
            return PausePointPersistedRecord.FromSchema(
                registryId,
                new EnablePausePointSchema { Id = registryId, TimeoutSeconds = 30, Persist = true });
        }

        private static string[] RegistryIdsOf(IReadOnlyList<PausePointPersistedRecord> records)
        {
            return records.Select(record => record.RegistryId).ToArray();
        }

        private static async Task<PausePointResponse> EnableByIdAsync(string id, bool persist)
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = id,
                ["timeoutSeconds"] = 30,
                ["persist"] = persist
            };

            return (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);
        }

        private static async Task<PausePointResponse> EnableByFileLineAsync(string file, int line, bool persist)
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["file"] = file,
                ["line"] = line,
                ["timeoutSeconds"] = 30,
                ["persist"] = persist
            };

            return (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);
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
            public IReadOnlyList<PausePointPersistedRecord> Saved { get; private set; } =
                Array.Empty<PausePointPersistedRecord>();

            public IReadOnlyList<PausePointPersistedRecord> Load()
            {
                return Saved;
            }

            public void Save(IReadOnlyList<PausePointPersistedRecord> records)
            {
                Saved = records;
            }
        }
    }
}
