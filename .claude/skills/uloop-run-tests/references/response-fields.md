# run-tests response fields

Returns JSON with:

- `Success` (boolean): Whether every test passed or was skipped; `false` when a test or suite failed or a test was inconclusive
- `Status` (string): Machine-readable execution status such as `Passed`, `Failed`, `Inconclusive`, `NoTestsFound`, `ExecutionFailed`, or `NothingToRerun` (`--rerun-failed` found nothing to rerun; `Success` is `true`)
- `HasFailures` (boolean): Whether any discovered test or suite failed
- `Message` (string): Summary message
- `NoTestsFound` (boolean): Whether Unity Test Runner discovered zero matching tests
- `NoTestsFoundExplanation` (string): Agent-facing explanation when `NoTestsFound` is true; empty otherwise. When `--rerun-failed` finds that every recorded test was renamed or removed, it says that instead of the test-assembly advice.
- `CompletedAt` (string): ISO timestamp when the run finished
- `TestCount` (number): Total tests executed
- `PassedCount` (number): Passed tests
- `FailedCount` (number): Failed tests
- `SkippedCount` (number): Skipped tests
- `InconclusiveCount` (number): Inconclusive tests (an `Assume` was not met)
- `XmlPath` (string or null): Path to NUnit XML result file. `null` when no XML was saved (typically on `Success: true`); set only when a test or suite failed or a test was inconclusive and the file exists on disk.
- `ClearedPausePointIds` (string[], optional): IDs of pause points that were cleared before test execution. Omitted from JSON when no pause points were active.
- `FailedTests` (array, optional): Up to 10 failed leaf tests with `FullName`, `Message`, and when the stack trace contains a path:line location, `File` and `Line`. Omitted when no tests failed. When `FailedCount` is greater than 10, `Message` ends with `first 10 of N failures listed; see XmlPath for full results.`
- `SkippedTests` (string[], optional): Up to 10 full names of skipped leaf tests. Omitted when no tests were skipped. When `SkippedCount` is greater than 10, only the first 10 names are listed.
- `InconclusiveTests` (array, optional): Up to 10 inconclusive leaf tests with `FullName` and `Message`. Omitted when no test was inconclusive. When `InconclusiveCount` is greater than 10, only the first 10 are listed; the XML at `XmlPath` has every message.
- `FailedSuites` (array, optional): Up to 10 suites that failed outside their tests (e.g. a `OneTimeSetUp` or `OneTimeTearDown` threw), with the `FailedTests` fields. The run is `Failed` even when `FailedCount` is 0. Omitted when none.
- `ProposedTestAsmdef` (object, optional): `AssetPath` and `Content` of a ready-to-write test `.asmdef` (test-assembly wiring plus references to the project's assemblies under test). Present only when an unfiltered run found no tests and no test assembly exists for the TestMode.
- `CompileNote` (string, optional): States that the automatic compile ran and succeeded before the tests and names `--skip-compile` as the opt-out. When the compile response carried a Warning (for example active hot-reload changes dropped by the domain reload), the note repeats it. Omitted when `--skip-compile` was passed; a failed compile returns the compile error response instead.
- `RerunTargetCount` (number, optional): Number of tests and fixtures a `--rerun-failed` run asked Unity to run. Omitted on other runs.
- `RerunSourceCompletedAt` (string, optional): `CompletedAt` of the recorded run whose failures `--rerun-failed` reran. Omitted on other runs.

Both are absent when `--respect-enter-play-mode-settings` entered Play Mode with a Domain Reload: the result is then recovered after the reload (see `Warning`), and a rerun whose recorded tests are all gone reports the generic no-tests message.

## XML Result File

Saved to `{project_root}/.uloop/outputs/TestResults/<timestamp>.xml`. What it records, including failed suites: `references/xml-results.md`.
