---
name: uloop-run-tests
toolName: run-tests
description: "Run Unity Test Runner and report detailed results. Use for EditMode/PlayMode tests, change verification, or failure diagnosis."
---

# uloop run-tests

Execute Unity Test Runner. When tests fail, NUnit XML results with error messages and stack traces are automatically saved. Read the XML file at `XmlPath` for detailed failure diagnosis.

Before running tests, run `uloop compile` first if you created, deleted, renamed, moved, or edited C# files, test files, `.asmdef`/`.asmref`, or package manifests since the last successful compile. This refreshes the AssetDatabase and surfaces compile errors before test execution.

Before executing tests, `uloop run-tests` saves unsaved loaded Scene changes and unsaved current Prefab Stage changes by default. If saving fails, it returns `Success: false`, keeps `TestCount` at `0`, lists the unsaved items in `Message`, and does not start the Unity Test Runner.

Active pause points are automatically cleared (the underlying code patches are removed as well) before test execution begins. Cleared IDs are reported in the response's `ClearedPausePointIds` field.

`NoTestsFound` means zero tests matched — not a test failure. Check `NoTestsFoundExplanation` and `Message` for asmdef hints.

## Usage

```bash
uloop run-tests [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--test-mode` | string | `EditMode` | Test mode: `EditMode`, `PlayMode` |
| `--filter-type` | string | `all` | Filter type: `all`, `exact`, `regex`, `assembly` |
| `--filter-value` | string | - | Filter value (test name, pattern, or assembly) |
| `--fail-on-unsaved-changes` | flag | - | Fail before test execution if unsaved editor changes remain instead of auto-saving them |
| `--timeout-seconds` | integer | `600` | Maximum seconds to wait for RunFinished before canceling the await (max `1500`). Increase for long suites; on timeout the Test Runner may still be running until stop handling lands |

## Output

Returns JSON with:

- `Success` (boolean): Whether all tests passed
- `Status` (string): Machine-readable execution status such as `Passed`, `Failed`, `NoTestsFound`, or `ExecutionFailed`
- `HasFailures` (boolean): Whether any discovered test failed
- `Message` (string): Summary message
- `NoTestsFound` (boolean): Whether Unity Test Runner discovered zero matching tests
- `NoTestsFoundExplanation` (string): Agent-facing explanation when `NoTestsFound` is true; empty otherwise
- `CompletedAt` (string): ISO timestamp when the run finished
- `TestCount` (number): Total tests executed
- `PassedCount` (number): Passed tests
- `FailedCount` (number): Failed tests
- `SkippedCount` (number): Skipped tests
- `XmlPath` (string): Path to NUnit XML result file. Empty string when no XML was saved (typically on `Success: true`); populated only when tests failed and the XML file exists on disk.
- `ClearedPausePointIds` (string[], optional): IDs of pause points that were cleared before test execution. Omitted from JSON when no pause points were active.

### XML Result File

When tests fail, NUnit XML results are automatically saved to `{project_root}/.uloop/outputs/TestResults/<timestamp>.xml`. The XML contains per-test-case results including:

- Test name and full name
- Pass/fail/skip status and duration
- For failed tests: `<message>` (assertion error) and `<stack-trace>`
