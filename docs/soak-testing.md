# Soak testing with the soak-loop harness

`scripts/soak-loop.sh` (macOS/Linux) and `scripts/soak-loop.ps1` (Windows) are
an endurance harness that exercises the `uloop` CLI against a real Unity
project for hundreds of iterations. They answer the question a normal test
suite cannot: does uloop stay correct, responsive, and leak-free across many
script compilations, domain reloads, PlayMode cycles, test runs, and full
editor restarts?

Both variants run the same iteration plan and write the same CSV schema, so
results from either platform are directly comparable. Two behaviours are so
far implemented only in the PowerShell variant: the Debug code-optimization
setup below, and reporting known-benign outcomes as `TOLERATED` instead of
`FAIL`. The shell variant applies the same tolerance rules to its
pass/fail decisions but still prints them as `FAIL`.

It works against **any** Unity project that has the uloop package installed —
point it at a large production project for the most realistic signal.

## Prerequisites

- The uloop package installed in the target project, and a `uloop` dispatcher
  on `PATH` (or set `ULOOP_BIN`, see below).
- The Unity Editor does not have to be running: if no editor has the target
  project open, the harness launches one via `uloop launch`; a running but
  busy editor (importing/compiling) is waited on for up to 15 minutes.
- macOS/Linux (`soak-loop.sh`): POSIX sh, plus `jq` when the PlayMode cycle is
  enabled (`--pause-every` > 0). Metrics are sampled with `ps`/`pgrep`/`perl`,
  so this variant is not expected to run on Windows.
- Windows (`soak-loop.ps1`): Windows PowerShell 5.1 or PowerShell 7. No `jq` —
  responses are parsed with `ConvertFrom-Json`. Metrics come from
  `Win32_Process`, so `unity_rss_kb` is `Unity.exe`'s working set.

## Quick start

```sh
sh scripts/soak-loop.sh \
  --project-path /path/to/unity-project \
  --iterations 20 \
  --test-assembly YourProject.Tests.Editor
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\soak-loop.ps1 `
  -ProjectPath C:\path\to\unity-project `
  -Iterations 20 `
  -TestAssembly YourProject.Tests.Editor
```

Options and defaults (also printed by running the shell script without
arguments; the PowerShell parameters are the same names in PascalCase, e.g.
`--restart-every` is `-RestartEvery`):

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

PowerShell-only parameters:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `-CommandTimeoutSeconds` | 600 | Kill bound for one uloop call. Raise it for large projects and parallel soaks (see below) |
| `-CompileWaitTimeoutSeconds` | 0 (runner default) | Passed to every compile as `--compile-wait-timeout-seconds` when the pinned runner accepts it |
| `-KeepCodeOptimization` | off | Leave the editor's code optimization mode alone (see below) |

Environment: `ULOOP_BIN` selects the uloop binary (default: `uloop` from
`PATH`, the realistic released configuration). Point it at a `dist/` binary to
soak unreleased CLI code, per the Native Go CLI Validation rules in CLAUDE.md
(`dist\windows-amd64\uloop.exe` on Windows).

Every uloop call runs under a kill watchdog (10 minutes by default). A hung IPC
call — a frozen editor that accepted the connection but never answers — is
recorded as one finite failure (exit code `124`) so the consecutive-failure
recovery can fire instead of blocking an unattended soak forever. The default
is not enough for every project: a forced full recompile of one large project
took ~8 minutes on its own, and over 10 minutes with three editors compiling in
parallel, so raise `-CommandTimeoutSeconds` for runs like that.

A compile that outlives the runner's own wait returns `COMPILE_WAIT_TIMEOUT`
while Unity keeps compiling. Runners from `--compile-wait-timeout-seconds`
onwards let that wait be raised: pass `-CompileWaitTimeoutSeconds` and the
harness appends the flag to every compile, lifting its kill watchdog above the
new wait so a timeout can still be reported rather than killed. Against an
older pinned runner the request is logged and ignored. Values above 1200 exceed
Unity's 20-minute result retention, which weakens the post-timeout recovery.

uloop is single-flight, so a command issued while Unity still runs an earlier
one is refused at dispatch with `UNITY_SERVER_BUSY`. That is back-pressure from
the harness's own previous command, not a defect: the PowerShell variant waits
(up to 20 tries, 30s apart) and records only the decisive attempt, so one slow
compile no longer fails every command behind it.

Heavy operations are also kept off the same iteration: a `run-tests` due on the
same iteration as `compile --force-recompile` is deferred by one iteration
(except on the last iteration, where deferring would drop the run entirely).
Stacking them serialises a full rebuild in front of the test run and can leave
Unity compiling while the tests try to start.

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

## Code optimization (pause points)

`enable-pause-point` resolves a file and line through information Unity only
keeps under **Debug** code optimization. Against a Release editor every arm
call fails on that precondition, so the whole PlayMode cycle would soak a
guaranteed failure. When `-PauseEvery` > 0, `soak-loop.ps1` reads the editor's
mode at setup, switches it to Debug if needed, and restores the original mode
when the run ends. Both switches trigger a full recompile — the setup compile
absorbs the first one by retrying past `Compilation is already in progress`,
and the restoring recompile runs in the background after the summary. Pass
`-KeepCodeOptimization` to leave the setting alone — expect every pause-point
command to fail if the project stays on Release.

Debug code optimization also makes compiles slower (on one large project,
`compile` averaged ~53s under Release and ~82s under Debug), so only compare
latency between runs that used the same mode.

## Expected (tolerated) failures

The harness distinguishes uloop defects from known-benign outcomes. These do
NOT fail an iteration; they are logged as `TOLERATED` (not `FAIL`) and counted
in the summary's `tolerated` column instead of `fails`. `commands.csv` keeps
the raw non-zero exit code either way, so it remains the unfiltered record for
graphing.

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

- `run.log` — human-readable progress, `FAIL` (and, in the PowerShell variant,
  `TOLERATED`) lines, and a per-command summary table printed at the end
  (runs / fails / avg ms; the PowerShell variant adds a `tolerated` column).
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
