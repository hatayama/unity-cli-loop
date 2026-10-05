using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the per-test-mode record of the last completed run that --rerun-failed reads.
    /// </summary>
    public sealed class RunTestsLastRunRecordStoreTests
    {
        private const string CompletedAt = "2026-01-02T03:04:05.0000000Z";

        private string _recordDirectory;
        private RunTestsLastRunRecordStore _store;

        [SetUp]
        public void SetUp()
        {
            _recordDirectory = Path.Combine(Path.GetTempPath(), "uloop-last-run-" + Guid.NewGuid().ToString("N"));
            _store = new RunTestsLastRunRecordStore(_recordDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_recordDirectory))
            {
                Directory.Delete(_recordDirectory, true);
            }
        }

        /// <summary>
        /// What: a written record reads back as Found with the same fields and the same target order.
        /// </summary>
        [Test]
        public void Read_AfterTryWrite_ReturnsFoundWithWrittenFields()
        {
            string[] targets = { "Ns.C.Second", "Ns.C.M(1,\"a\")", "Ns.FixtureWhoseOneTimeTearDownThrew" };

            bool written = _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, targets);
            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            Assert.That(written, Is.True);
            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Found));
            Assert.That(read.Record.FormatVersion, Is.EqualTo(1));
            Assert.That(read.Record.TestMode, Is.EqualTo("EditMode"));
            Assert.That(read.Record.CompletedAt, Is.EqualTo(CompletedAt));
            Assert.That(read.Record.RerunTargets, Is.EqualTo(targets));
        }

        /// <summary>
        /// What: a second write replaces the first record instead of failing on the existing file.
        /// </summary>
        [Test]
        public void Read_AfterSecondTryWrite_ReturnsSecondRecord()
        {
            _store.TryWrite(UnityCliLoopTestMode.EditMode, "2026-01-01T00:00:00.0000000Z", new[] { "Ns.C.Old" });

            bool written = _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.New" });
            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            Assert.That(written, Is.True);
            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Found));
            Assert.That(read.Record.CompletedAt, Is.EqualTo(CompletedAt));
            Assert.That(read.Record.RerunTargets, Is.EqualTo(new[] { "Ns.C.New" }));
        }

        /// <summary>
        /// What: an EditMode record is not visible to a PlayMode read.
        /// </summary>
        [Test]
        public void Read_ForOtherTestMode_ReturnsMissing()
        {
            _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.M" });

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.PlayMode);

            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Missing));
            Assert.That(read.Record, Is.Null);
        }

        /// <summary>
        /// What: a test mode that was never recorded reads as Missing.
        /// </summary>
        [Test]
        public void Read_WithoutRecordFile_ReturnsMissing()
        {
            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Missing));
            Assert.That(read.Record, Is.Null);
        }

        /// <summary>
        /// What: the valid fixture that the Unreadable tests break one field of reads as Found.
        /// </summary>
        [Test]
        public void Read_WithValidFixture_ReturnsFound()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, CreateRecordJson());

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Found));
            Assert.That(read.Record.RerunTargets, Is.EqualTo(new[] { "Ns.C.M" }));
        }

        /// <summary>
        /// What: a record file that is not JSON reads as Unreadable.
        /// </summary>
        [Test]
        public void Read_WithInvalidJson_ReturnsUnreadable()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, "{ \"FormatVersion\": 1, ");

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            AssertUnreadable(read, "invalid JSON");
        }

        /// <summary>
        /// What: a record written by another format version reads as Unreadable.
        /// </summary>
        [Test]
        public void Read_WithUnsupportedFormatVersion_ReturnsUnreadable()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, CreateRecordJson(formatVersion: "2"));

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            AssertUnreadable(read, "FormatVersion 2");
        }

        /// <summary>
        /// What: a record whose TestMode differs from the file it was read for reads as Unreadable.
        /// </summary>
        [Test]
        public void Read_WithMismatchedTestMode_ReturnsUnreadable()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, CreateRecordJson(testMode: "\"PlayMode\""));

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            AssertUnreadable(read, "PlayMode");
        }

        /// <summary>
        /// What: a record without RerunTargets reads as Unreadable instead of as nothing to rerun.
        /// </summary>
        [Test]
        public void Read_WithoutRerunTargets_ReturnsUnreadable()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, CreateRecordJson(rerunTargets: null));

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            AssertUnreadable(read, "RerunTargets is missing");
        }

        /// <summary>
        /// What: a record with an empty target name reads as Unreadable.
        /// </summary>
        [Test]
        public void Read_WithEmptyRerunTargetName_ReturnsUnreadable()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, CreateRecordJson(rerunTargets: "[\"Ns.C.M\", \"\"]"));

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            AssertUnreadable(read, "empty name");
        }

        /// <summary>
        /// What: a record without CompletedAt reads as Unreadable.
        /// </summary>
        [Test]
        public void Read_WithoutCompletedAt_ReturnsUnreadable()
        {
            WriteRawRecord(UnityCliLoopTestMode.EditMode, CreateRecordJson(completedAt: null));

            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.EditMode);

            AssertUnreadable(read, "CompletedAt is missing");
        }

        /// <summary>
        /// What: Delete removes the record, and deleting an already removed record does not throw.
        /// </summary>
        [Test]
        public void Delete_AfterTryWrite_LeavesRecordMissingAndToleratesSecondDelete()
        {
            _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.M" });

            _store.Delete(UnityCliLoopTestMode.EditMode);

            Assert.That(
                _store.Read(UnityCliLoopTestMode.EditMode).Status,
                Is.EqualTo(RunTestsLastRunRecordReadStatus.Missing));
            Assert.DoesNotThrow(() => _store.Delete(UnityCliLoopTestMode.EditMode));
        }

        /// <summary>
        /// What: Delete before the record directory exists, as on a project's first run, does not throw.
        /// </summary>
        [Test]
        public void Delete_WithoutRecordDirectory_DoesNotThrow()
        {
            Assert.That(Directory.Exists(_recordDirectory), Is.False);

            Assert.DoesNotThrow(() => _store.Delete(UnityCliLoopTestMode.EditMode));
        }

        /// <summary>
        /// What: deleting the EditMode record keeps the PlayMode record.
        /// </summary>
        [Test]
        public void Delete_OfOneTestMode_KeepsOtherTestModeRecord()
        {
            _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.EditModeTest" });
            _store.TryWrite(UnityCliLoopTestMode.PlayMode, CompletedAt, new[] { "Ns.C.PlayModeTest" });

            _store.Delete(UnityCliLoopTestMode.EditMode);
            RunTestsLastRunRecordReadResult read = _store.Read(UnityCliLoopTestMode.PlayMode);

            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Found));
            Assert.That(read.Record.RerunTargets, Is.EqualTo(new[] { "Ns.C.PlayModeTest" }));
        }

        /// <summary>
        /// What: Delete reports a record path it cannot clear as an I/O failure instead of ignoring it.
        /// </summary>
        [Test]
        public void Delete_WhenRecordPathIsOccupiedByDirectory_ThrowsFileAccessFailure()
        {
            OccupyRecordPathWithDirectory(UnityCliLoopTestMode.EditMode);

            Exception thrown = null;
            try
            {
                _store.Delete(UnityCliLoopTestMode.EditMode);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            Assert.That(
                thrown is IOException || thrown is UnauthorizedAccessException,
                Is.True,
                "Expected IOException or UnauthorizedAccessException but got: " + thrown);
        }

        /// <summary>
        /// What: writing a new record and then replacing it leaves no temporary file behind.
        /// </summary>
        [Test]
        public void TryWrite_WhenCreatingAndReplacingRecord_LeavesNoTemporaryFile()
        {
            _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.First" });
            _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.Second" });

            Assert.That(Directory.GetFiles(_recordDirectory, "*.tmp"), Is.Empty);
        }

        /// <summary>
        /// What: a write that cannot reach the record path returns false without throwing or leaving a temporary file.
        /// </summary>
        [Test]
        public void TryWrite_WhenRecordPathIsOccupiedByDirectory_ReturnsFalseAndLeavesNoTemporaryFile()
        {
            OccupyRecordPathWithDirectory(UnityCliLoopTestMode.EditMode);

            bool written = _store.TryWrite(UnityCliLoopTestMode.EditMode, CompletedAt, new[] { "Ns.C.M" });

            Assert.That(written, Is.False);
            Assert.That(Directory.GetFiles(_recordDirectory, "*.tmp"), Is.Empty);
        }

        private void WriteRawRecord(UnityCliLoopTestMode testMode, string json)
        {
            Directory.CreateDirectory(_recordDirectory);
            File.WriteAllText(_store.GetRecordPath(testMode), json);
        }

        private void OccupyRecordPathWithDirectory(UnityCliLoopTestMode testMode)
        {
            string recordPath = _store.GetRecordPath(testMode);
            Directory.CreateDirectory(recordPath);
            File.WriteAllText(Path.Combine(recordPath, "keep.txt"), "occupied");
        }

        // Builds a valid record and lets each caller break or omit exactly one member; null omits it.
        private static string CreateRecordJson(
            string formatVersion = "1",
            string testMode = "\"EditMode\"",
            string completedAt = "\"" + CompletedAt + "\"",
            string rerunTargets = "[\"Ns.C.M\"]")
        {
            List<string> members = new();
            AddMember(members, "FormatVersion", formatVersion);
            AddMember(members, "TestMode", testMode);
            AddMember(members, "CompletedAt", completedAt);
            AddMember(members, "RerunTargets", rerunTargets);
            return "{" + string.Join(",", members) + "}";
        }

        private static void AddMember(List<string> members, string name, string jsonValue)
        {
            if (jsonValue == null)
            {
                return;
            }

            members.Add("\"" + name + "\":" + jsonValue);
        }

        private static void AssertUnreadable(RunTestsLastRunRecordReadResult read, string expectedReasonFragment)
        {
            Assert.That(read.Status, Is.EqualTo(RunTestsLastRunRecordReadStatus.Unreadable));
            Assert.That(read.Record, Is.Null);
            Assert.That(read.UnreadableReason, Does.Contain(expectedReasonFragment));
        }
    }
}
