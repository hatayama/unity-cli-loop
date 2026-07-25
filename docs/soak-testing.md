# Soak testing with scripts/soak-loop.sh

`scripts/soak-loop.sh` is an endurance harness that exercises the `uloop` CLI
against a real Unity project for hundreds of iterations. It answers the
question a normal test suite cannot: does uloop stay correct, responsive, and
leak-free across many script compilations, domain reloads, PlayMode cycles,
test runs, and full editor restarts?

It works against **any** Unity project that has the uloop package installed —
point it at a large production project for the most realistic signal.

## Prerequisites

- The uloop package installed in the target project, and a `uloop` dispatcher
  on `PATH` (or set `ULOOP_BIN`, see below).
- The Unity Editor does not have to be running: if no editor has the target
  project open, the harness launches one via `uloop launch`; a running but
  busy editor (importing/compiling) is waited on for up to 15 minutes.
- `jq`, when the PlayMode cycle is enabled (`--pause-every` > 0).
- macOS or Linux. The harness is POSIX sh but samples metrics with
  `ps`/`pgrep`/`perl`; it is not expected to run on Windows.

## Quick start

```sh
sh scripts/soak-loop.sh \
  --project-path /path/to/unity-project \
  --iterations 20 \
  --test-assembly YourProject.Tests.Editor
```

Options and defaults (also printed by running the script without arguments):

| Option | Default | Meaning |
| --- | --- | --- |
| `--project-path` | (required) | Target Unity project root |
| `--iterations` | 100 | Total iterations |
| `--restart-every` | 25 | Full editor restart (`uloop launch -r`) cadence; 0 = never |
| `--force-every` | 10 | `compile --force-recompile` cadence; 0 = never |
| `--pause-every` | 5 | PlayMode cycle (UI click + pause-point) cadence; 0 = never |
| `--tests-every` | 10 | `run-tests` cadence; 0 = never |
| `--test-assembly` | (required when tests enabled) | Assembly for `run-tests --filter-type assembly`. Always scope to one assembly — never soak the full suite of a large project |
| `--sleep-seconds` | 0 | Pause between iterations |
| `--out-dir` | `./uloop-soak-results/<timestamp>` | Results directory |

Environment: `ULOOP_BIN` selects the uloop binary (default: `uloop` from
`PATH`, the realistic released configuration). Point it at a `dist/` binary to
soak unreleased CLI code, per the Native Go CLI Validation rules in CLAUDE.md.

## What one iteration does

1. Rewrites a scratch editor script under `Assets/UloopSoak/Editor/` so every
   iteration forces a genuine compilation + domain reload.
2. Runs the commands that must survive the reload crossing: `compile`,
   `get-logs`, `get-hierarchy`, `screenshot`, `execute-dynamic-code`.
3. On cadence, additionally runs a forced recompile, a PlayMode cycle
   (rebuild the soak scene → Play → annotated-screenshot → simulated UI click
   → verify the click registered → pause-point hit with variable capture →
   Stop), a scoped `run-tests`, and a full editor restart.
4. Samples editor RSS, leftover project-runner processes, and the
   `.uloop/outputs` directory size as leak signals.

Three consecutive failing iterations trigger one recovery restart; a second
consecutive trip aborts the run.

## Expected (tolerated) failures

The harness distinguishes uloop defects from known-benign outcomes. These do
NOT fail an iteration:

- **Forced recompile without a definitive result** — designed behavior
  (`ForceCompileUnknownResult`); the follow-up plain compile is what counts.
- **Red project tests** — the harness measures whether uloop transported and
  completed the run (a `TestCount` in the response), not whether the target
  project's tests are green.
- **"Compilation is already in progress"** — an explicit compile right after
  an editor (re)start can collide with Unity's startup compilation; the
  harness retries once after 10 seconds.

## Scene safety

The PlayMode cycle rebuilds a disposable scene at `Assets/UloopSoak/` from
scratch before every cycle. Because `EditorSceneManager.NewScene`/`OpenScene`
silently discard unsaved changes (the save dialog is a courtesy of Unity's UI
entry points, not the script API), the harness guards itself: if the active
scene is a USER scene with unsaved changes, the cycle is skipped and logged
instead of destroying work.

The one exception is right after a harness-initiated restart. The bypass is
safe not because the harness can attribute the changes, but because of what a
restart implies: quitting the editor already destroys any unsaved user edits,
so a scene that is dirty immediately after the editor comes back up can only
have been modified by project tooling during startup — there has been no
opportunity for user work to exist yet. Only at that moment is the dirt
discarded to restore the soak scene; every regular cycle goes through the
guard.

Still: save your own scene changes before running, and do not edit scenes in
the target project while a soak is in progress — edits made between a restart
and the harness's scene restore are not protected.

## Reading the results

The output directory contains:

- `run.log` — human-readable progress, `FAIL` lines, and a per-command
  summary table (runs / fails / avg ms) printed at the end.
- `commands.csv` — `epoch_ms,iteration,command,exit_code,duration_ms,payload_bytes`
  for every command, for graphing latency drift and failure rate over time.
- `metrics.csv` — `epoch_ms,iteration,unity_rss_kb,project_runner_procs,outputs_dir_kb`
  per iteration. Look for monotonic RSS growth (leak), runner processes that
  never return to 0, or unbounded outputs growth.

## Parallel soaks

Multiple instances can run concurrently as long as each targets a **different**
project checkout (each editor is its own IPC server). Expect heavier commands
to slow down under parallel load (in a 5-editor run on one machine, compile
went from ~23s solo to ~37–51s) while lightweight commands stay sub-second.
Never point two instances — or any two `run-tests` invocations — at the same
editor.

## Cleanup

The harness leaves `Assets/UloopSoak/` (scratch script, ticker, button probe,
scene) in the target project so a follow-up run can be compared against the
same state. Delete that folder and its `.meta` manually when finished. Do not
revert the target project's `manifest.json`/`packages-lock.json` changes if
they came from installing the uloop package itself.
