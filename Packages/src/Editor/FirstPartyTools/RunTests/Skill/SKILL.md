---
name: uloop-run-tests
toolName: run-tests
description: "Run Unity Test Runner and report detailed results. Use for EditMode/PlayMode tests, change verification, or failure diagnosis."
---

# uloop run-tests

Execute Unity Test Runner. When tests fail, NUnit XML results with error messages and stack traces are automatically saved. Read the XML file at `xmlPath` for detailed failure diagnosis.

Before running tests, run `uloop compile` first if you edited C# files, test files, `.asmdef`/`.asmref`, or package manifests since the last successful compile. This refreshes the AssetDatabase and surfaces compile errors before test execution.

Before executing tests, `uloop run-tests` saves unsaved loaded Scene changes and unsaved current Prefab Stage changes by default. If saving fails, it returns `success: false`, keeps `testCount` at `0`, lists the unsaved items in `message`, and does not start the Unity Test Runner.

`NoTestsFound` means zero tests matched — not a test failure. Check `noTestsFoundExplanation` and `message` for asmdef hints.

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

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Run all EditMode tests
uloop run-tests

# Run PlayMode tests
uloop run-tests --test-mode PlayMode

# Fail instead of auto-saving when editor changes are unsaved
uloop run-tests --fail-on-unsaved-changes

# Run specific test
uloop run-tests --filter-type exact --filter-value "MyTest.TestMethod"

# Run tests matching pattern
uloop run-tests --filter-type regex --filter-value ".*Integration.*"
```

## Output

Returns JSON with:
- `success` (boolean): Whether all tests passed
- `status` (string): Machine-readable execution status such as `Passed`, `Failed`, `NoTestsFound`, or `ExecutionFailed`
- `hasFailures` (boolean): Whether any discovered test failed
- `message` (string): Summary message
- `noTestsFound` (boolean): Whether Unity Test Runner discovered zero matching tests
- `noTestsFoundExplanation` (string): Agent-facing explanation when `noTestsFound` is true; empty otherwise
- `completedAt` (string): ISO timestamp when the run finished
- `testCount` (number): Total tests executed
- `passedCount` (number): Passed tests
- `failedCount` (number): Failed tests
- `skippedCount` (number): Skipped tests
- `xmlPath` (string): Path to NUnit XML result file. Empty string when no XML was saved (typically on `success: true`); populated only when tests failed and the XML file exists on disk.

### XML Result File

When tests fail, NUnit XML results are automatically saved to `{project_root}/.uloop/outputs/TestResults/<timestamp>.xml`. The XML contains per-test-case results including:
- Test name and full name
- Pass/fail/skip status and duration
- For failed tests: `<message>` (assertion error) and `<stack-trace>`
