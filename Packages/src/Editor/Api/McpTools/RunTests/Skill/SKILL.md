---
name: uloop-run-tests
description: "Execute Unity Test Runner and get detailed results. Use when you need to: (1) Run EditMode or PlayMode unit tests, (2) Verify code changes pass all tests, (3) Diagnose test failures with error messages and stack traces. Single-flight only — never run multiple `uloop run-tests` in parallel."
---

# uloop run-tests

Execute Unity Test Runner. This command requires the Unity Test Framework package (`com.unity.test-framework`). If that package is not installed, the command returns `Success: false` with an unsupported message and does not affect the other Unity CLI Loop tools.

When tests fail, NUnit XML results with error messages and stack traces are automatically saved. Read the XML file at `XmlPath` for detailed failure diagnosis.

Before executing tests, `uloop run-tests` checks for unsaved loaded Scene changes and unsaved current Prefab Stage changes. If any are found, it returns `Success: false`, keeps `TestCount` at `0`, lists the unsaved items in `Message`, and does not start the Unity Test Runner. Save or discard those editor changes, then rerun the command. Use `--save-before-run true` only when the user explicitly asks to save editor changes before continuing.

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
| `--save-before-run` | boolean | `false` | Save unsaved loaded Scene changes and current Prefab Stage changes before running tests |

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

# Save explicitly approved editor changes before running tests
uloop run-tests --save-before-run true

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
- `XmlPath` (string | null): Path to NUnit XML result file. `null` when no XML was saved; populated only when tests failed and the XML file exists on disk.

### XML Result File

When tests fail, NUnit XML results are automatically saved to `{project_root}/.uloop/outputs/TestResults/<timestamp>.xml`. The XML contains per-test-case results including:
- Test name and full name
- Pass/fail/skip status and duration
- For failed tests: `<message>` (assertion error) and `<stack-trace>`
