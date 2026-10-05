using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// JSON shape of the record that a completed run-tests execution leaves for --rerun-failed.
    /// </summary>
    internal sealed class RunTestsLastRunRecord
    {
        internal const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; }

        public string TestMode { get; set; }

        public string CompletedAt { get; set; }

        /// <summary>
        /// Names to pass to the Unity testNames filter to rerun every failed or inconclusive test of the run.
        /// </summary>
        public string[] RerunTargets { get; set; }
    }

    /// <summary>
    /// Outcome of reading the last-run record of one test mode.
    /// </summary>
    internal enum RunTestsLastRunRecordReadStatus
    {
        Missing,
        Unreadable,
        Found
    }

    /// <summary>
    /// Result of reading the last-run record: Found carries the record, Unreadable carries the reason.
    /// </summary>
    internal readonly struct RunTestsLastRunRecordReadResult
    {
        public RunTestsLastRunRecordReadStatus Status { get; }

        /// <summary>
        /// The record when Status is Found; null otherwise.
        /// </summary>
        public RunTestsLastRunRecord Record { get; }

        /// <summary>
        /// Why the record was rejected when Status is Unreadable; empty otherwise.
        /// </summary>
        public string UnreadableReason { get; }

        private RunTestsLastRunRecordReadResult(
            RunTestsLastRunRecordReadStatus status,
            RunTestsLastRunRecord record,
            string unreadableReason)
        {
            Status = status;
            Record = record;
            UnreadableReason = unreadableReason;
        }

        public static RunTestsLastRunRecordReadResult Missing()
        {
            return new RunTestsLastRunRecordReadResult(
                RunTestsLastRunRecordReadStatus.Missing,
                null,
                string.Empty);
        }

        public static RunTestsLastRunRecordReadResult Unreadable(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw new ArgumentException("An unreadable record must say why.", nameof(reason));
            }

            return new RunTestsLastRunRecordReadResult(
                RunTestsLastRunRecordReadStatus.Unreadable,
                null,
                reason);
        }

        public static RunTestsLastRunRecordReadResult Found(RunTestsLastRunRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            return new RunTestsLastRunRecordReadResult(
                RunTestsLastRunRecordReadStatus.Found,
                record,
                string.Empty);
        }
    }
}
