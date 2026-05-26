---
name: uloop-run-tests
toolName: run-tests
description: "Run Unity Test Runner and report detailed results. Use for EditMode/PlayMode tests, change verification, or failure diagnosis."
---

# uloop run-tests

Execute Unity Test Runner. When tests fail, NUnit XML results with error messages and stack traces are automatically saved. Read the XML file at `XmlPath` for detailed failure diagnosis.

Before running `uloop run-tests`, run `uloop compile` for the same Unity project when the current task created, deleted, renamed, moved, or edited C# source files, test files, `.asmdef`, `.asmref`, package manifest files, or scripting define settings. This refreshes the AssetDatabase, lets Unity discover new tests, and surfaces compile errors before test execution. You may skip this compile step when rerunning tests without code or assembly-definition changes since the last successful compile.

Before executing tests, `uloop run-tests` saves unsaved loaded Scene changes and unsaved current Prefab Stage changes by default. If saving fails, it returns `Success: false`, keeps `TestCount` at `0`, lists the unsaved items in `Message`, and does not start the Unity Test Runner.

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
- `Success` (boolean): Whether all tests passed
- `Message` (string): Summary message
- `CompletedAt` (string): ISO timestamp when the run finished
- `TestCount` (number): Total tests executed
- `PassedCount` (number): Passed tests
- `FailedCount` (number): Failed tests
- `SkippedCount` (number): Skipped tests
- `XmlPath` (string): Path to NUnit XML result file. Empty string when no XML was saved (typically on `Success: true`); populated only when tests failed and the XML file exists on disk.

### XML Result File

When tests fail, NUnit XML results are automatically saved to `{project_root}/.uloop/outputs/TestResults/<timestamp>.xml`. The XML contains per-test-case results including:
- Test name and full name
- Pass/fail/skip status and duration
- For failed tests: `<message>` (assertion error) and `<stack-trace>`
