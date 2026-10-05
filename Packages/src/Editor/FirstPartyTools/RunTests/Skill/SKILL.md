---
name: uloop-run-tests
toolName: run-tests
description: "Run Unity Test Runner and report detailed results. Use for EditMode/PlayMode tests, change verification, or failure diagnosis."
---

# uloop run-tests

Execute Unity Test Runner. When a test or suite fails or a test ends inconclusive, NUnit XML results with failure messages, stack traces, and inconclusive reasons are automatically saved. Read the XML file at `XmlPath` for detailed diagnosis.

`uloop run-tests` automatically compiles pending script changes before running tests. Pass `--skip-compile` only while validating active hot-reload patches, because the compile clears those patches; otherwise let the default compile surface errors and run against current scripts. `--skip-compile` skips only the CLI-side compile: Unity still imports script edits saved since the last compile, and that import reloads the domain as soon as the run releases its assembly lock, discarding active patches and ending the request.

Before executing tests, `uloop run-tests` handles unsaved loaded Scene and Prefab Stage changes according to `--unsaved-changes` (default `save`): `save` writes them first, `fail` stops if any remain, and `discard` reloads disk state so tests run against saved files. Untitled scenes cannot be discarded and fail. If the chosen mode cannot proceed, it returns `Success: false`, keeps `TestCount` at `0`, lists the items in `Message`, and does not start the Unity Test Runner.

Active pause points are automatically cleared (the underlying code patches are removed as well) before test execution begins. Cleared IDs are reported in the response's `ClearedPausePointIds` field.

A test run can end by discarding active hot-reload changes: script edits imported during the run are compiled when the test runner releases its assembly-reload lock, and that deferred domain reload wipes the patches even though the tests themselves ran patched. The response's Warning field reports this; re-apply 'uloop hot-reload' or bake the edits in with 'uloop compile' before the next Play or test run.

`NoTestsFound` means zero tests matched — not a test failure. Check `NoTestsFoundExplanation` and `Message` for asmdef hints. When an unfiltered run finds no tests and the project has no test assembly for the TestMode, `ProposedTestAsmdef` carries a ready-to-write `.asmdef`: save `Content` at `AssetPath`, move the test scripts under that folder, then compile and rerun.

`Status: Inconclusive` means no test failed but at least one could not meet an `Assume`; `Success` is `false`, as Unity's batchmode test run also fails for it. `InconclusiveTests` names them with the assumption's message. A test that cannot run in this environment should call `Assert.Ignore` so it reports as skipped.

## Usage

```bash
uloop run-tests [options]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--test-mode` | string | `EditMode` | Test mode: `EditMode`, `PlayMode` |
| `--filter-type` | string | `all` | Filter type: `all`, `exact`, `regex`, `assembly`, `class` |
| `--filter-value` | string | - | Filter value (test name, pattern, assembly, or class name) |
| `--unsaved-changes` | string | `save` | `save` writes unsaved Scene/Prefab Stage changes; `fail` stops if any remain; `discard` reloads disk state (Untitled scenes fail) |
| `--skip-compile` | flag | - | Skip the CLI-side compile before running tests; use only while validating active hot-reload patches. Unity still imports script edits saved since the last compile. |
| `--timeout-seconds` | integer | `600` | Maximum seconds to wait for RunFinished before canceling the await (max `1500`). Increase for long suites; on timeout the Test Runner may still be running until stop handling lands |
| `--respect-enter-play-mode-settings` | flag | - | PlayMode only: keep the project's Enter Play Mode settings instead of forcing Domain Reload off. A Domain Reload during the run is survived; the result is recovered after the reload. Use for projects whose libraries require a Domain Reload on Play entry. |
| `--rerun-failed` | flag | - | Rerun only the tests that failed or were inconclusive in the most recent completed run of the same --test-mode (whole fixtures for a failed OneTimeSetUp/OneTimeTearDown). Cannot be combined with --filter-type or --filter-value |

By default PlayMode still forces Domain Reload off. With `--respect-enter-play-mode-settings`, a Domain Reload may run and the command takes longer; pause-point and hot-reload notes are omitted from a result recovered after reload. Canceling the CLI (Ctrl-C) does not stop the Unity-side run on this path.

exact matches the full test name (Namespace.Class.Method). class runs every test of one class by bare or namespace-qualified name, e.g. --filter-type class --filter-value PlayerTests; the name is matched literally and whole, so PlayerTests does not run EnemyPlayerTests. regex matches a .NET regex against full test names, e.g. --filter-type regex --filter-value '^MyGame\.Tests\.'

`--rerun-failed` reads the record that every completed run writes for its test mode, so it reruns the failures of the most recent completed run, filtered or not. A run that timed out or was cancelled leaves no record. With nothing recorded as failed it returns `Status: NothingToRerun` without running; to tell a flaky test from a real failure, run `--rerun-failed` again after a failure.

## Output

Returns JSON. `Success`, `Status`, `Message`, `FailedTests` (up to 10), and `XmlPath` (set when a test or suite failed or was inconclusive) are usually enough; every field is described in `references/response-fields.md`, and the XML file in `references/xml-results.md`.
