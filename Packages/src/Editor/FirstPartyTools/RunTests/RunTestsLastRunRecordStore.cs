using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads and writes the record of the most recent completed run of each test mode, which
    /// --rerun-failed uses to pick the tests to run again.
    /// </summary>
    internal sealed class RunTestsLastRunRecordStore
    {
        private const string RecordFileNamePrefix = "last-run-";
        private const string RecordFileExtension = ".json";

        private readonly string _testResultsDirectory;

        internal RunTestsLastRunRecordStore(string testResultsDirectory)
        {
            if (string.IsNullOrEmpty(testResultsDirectory) || !Path.IsPathRooted(testResultsDirectory))
            {
                throw new ArgumentException(
                    "The record directory must be an absolute path.",
                    nameof(testResultsDirectory));
            }

            _testResultsDirectory = testResultsDirectory;
        }

        /// <summary>
        /// Creates the store for the open project's TestResults directory. Call on the main thread,
        /// because the project root is read from Application.dataPath.
        /// </summary>
        internal static RunTestsLastRunRecordStore CreateForProject()
        {
            return new RunTestsLastRunRecordStore(Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                UnityCliLoopConstants.OUTPUT_ROOT_DIR,
                UnityCliLoopConstants.TEST_RESULTS_DIR));
        }

        /// <summary>
        /// Whether an exception is the file-system failure that record I/O reports instead of crashing.
        /// </summary>
        internal static bool IsFileAccessFailure(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException;
        }

        internal string GetRecordPath(UnityCliLoopTestMode testMode)
        {
            return Path.Combine(_testResultsDirectory, RecordFileNamePrefix + testMode + RecordFileExtension);
        }

        /// <summary>
        /// Reads the record of the test mode. Anything other than a complete record of that test mode
        /// is Unreadable, so a caller never reruns from a record it cannot trust.
        /// </summary>
        internal RunTestsLastRunRecordReadResult Read(UnityCliLoopTestMode testMode)
        {
            string path = GetRecordPath(testMode);
            if (!File.Exists(path))
            {
                return RunTestsLastRunRecordReadResult.Missing();
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception) when (IsFileAccessFailure(exception))
            {
                return RunTestsLastRunRecordReadResult.Unreadable("cannot read file: " + exception.Message);
            }

            RunTestsLastRunRecord record;
            try
            {
                record = JsonConvert.DeserializeObject<RunTestsLastRunRecord>(json);
            }
            catch (JsonException exception)
            {
                return RunTestsLastRunRecordReadResult.Unreadable("invalid JSON: " + exception.Message);
            }

            return Validate(record, testMode);
        }

        /// <summary>
        /// Removes the record of the test mode; does nothing when there is none. File-system failures
        /// propagate, because running on with the old record in place would let it pass as the newest.
        /// </summary>
        internal void Delete(UnityCliLoopTestMode testMode)
        {
            string path = GetRecordPath(testMode);
            // Why not File.Delete unguarded: whether it ignores a TestResults directory that does not
            // exist yet, as on a project's first run, differs between runtimes (the Windows API reports
            // it as a missing path, not a missing file). Why also Directory.Exists: File.Exists is false
            // for a directory, and a path we cannot clear must fail here instead of passing as deleted.
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return;
            }

            File.Delete(path);
        }

        /// <summary>
        /// Writes the record of the test mode, replacing the previous one. Returns false and logs a
        /// warning on a file-system failure instead of throwing, because the run's result is still valid.
        /// </summary>
        internal bool TryWrite(UnityCliLoopTestMode testMode, string completedAt, string[] rerunTargets)
        {
            if (rerunTargets == null)
            {
                throw new ArgumentNullException(nameof(rerunTargets));
            }

            string path = GetRecordPath(testMode);
            // Why a unique temporary name: concurrent writers must not overwrite each other's partial file,
            // and the real name must never hold a partially written record.
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(_testResultsDirectory);
                RunTestsLastRunRecord record = new()
                {
                    FormatVersion = RunTestsLastRunRecord.CurrentFormatVersion,
                    TestMode = testMode.ToString(),
                    CompletedAt = completedAt,
                    RerunTargets = rerunTargets
                };
                File.WriteAllText(
                    temporaryPath,
                    JsonConvert.SerializeObject(record, Formatting.Indented),
                    new UTF8Encoding(false));
                MoveIntoPlace(temporaryPath, path);
                return true;
            }
            catch (Exception exception) when (IsFileAccessFailure(exception))
            {
                DeleteTemporaryFileQuietly(temporaryPath);
                UnityEngine.Debug.LogWarning("Failed to write the run-tests last-run record: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Records a run-tests result recovered after a domain reload, but only when it came from a
        /// PlayMode run. Returns whether a record was written.
        /// </summary>
        internal static bool TryRecordRecoveredPlayModeRun(
            RunTestsLastRunRecordStore store,
            SerializableTestResult result,
            bool isPlayModeRun)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            // Why not record a run without a result tree: it has no names to rerun.
            if (result.status == RunTestsExecutionStatus.ExecutionFailed)
            {
                return false;
            }

            if (!isPlayModeRun)
            {
                return false;
            }

            return store.TryWrite(UnityCliLoopTestMode.PlayMode, result.completedAt, result.rerunTargetFullNames);
        }

        private static RunTestsLastRunRecordReadResult Validate(
            RunTestsLastRunRecord record,
            UnityCliLoopTestMode testMode)
        {
            if (record == null)
            {
                return RunTestsLastRunRecordReadResult.Unreadable("empty file");
            }

            if (record.FormatVersion != RunTestsLastRunRecord.CurrentFormatVersion)
            {
                return RunTestsLastRunRecordReadResult.Unreadable(
                    "unsupported FormatVersion " + record.FormatVersion);
            }

            if (record.TestMode != testMode.ToString())
            {
                return RunTestsLastRunRecordReadResult.Unreadable(
                    "records " + record.TestMode + " instead of " + testMode);
            }

            if (record.RerunTargets == null)
            {
                return RunTestsLastRunRecordReadResult.Unreadable("RerunTargets is missing");
            }

            if (Array.Exists(record.RerunTargets, string.IsNullOrWhiteSpace))
            {
                return RunTestsLastRunRecordReadResult.Unreadable("RerunTargets contains an empty name");
            }

            if (string.IsNullOrWhiteSpace(record.CompletedAt))
            {
                return RunTestsLastRunRecordReadResult.Unreadable("CompletedAt is missing");
            }

            return RunTestsLastRunRecordReadResult.Found(record);
        }

        // Why two branches: File.Replace needs an existing destination, and the overwriting
        // File.Move overload is missing from Unity's API compatibility levels.
        private static void MoveIntoPlace(string temporaryPath, string path)
        {
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
                return;
            }

            File.Move(temporaryPath, path);
        }

        private static void DeleteTemporaryFileQuietly(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (IsFileAccessFailure(exception))
            {
                // Why swallow: the write already failed and is reported once; a leftover .tmp file
                // never passes for a record because Read only opens the record's own name.
            }
        }
    }
}
