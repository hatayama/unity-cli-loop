# VibeLogs: Tool-Side Logs for Investigations

Read this before concluding that a BUSY error, a hang, or an unexplained `uloop` result "cannot
be diagnosed because no logs were collected". The tool writes its own structured logs into the
Unity project, and they are easy to forget because they live outside the Editor console and are
not part of any command response. One investigation (2026-09-23) reported a BUSY freeze as
undiagnosable while the log that pinned it to a single stalled request was sitting in the
project the whole time.

## Where they are

Both sides of the tool write JSON-lines files into the same directory of the Unity project the
command ran against:

```
<PROJECT_ROOT>/.uloop/outputs/VibeLogs/
  unity_vibe_YYYYMMDD.json   # Unity Editor side (server, tools, domain reloads)
  cli_vibe_YYYYMMDD.json     # native CLI side
```

- The date in the file name is the **UTC** date, while each entry's `timestamp` is local time with
  its offset. An evening event in a zone ahead of UTC lands in the previous day's file, so search
  the neighboring day's file before deciding an event was not logged.
- `.uloop/*` is gitignored, so `git reset --hard` and `git clean` without `-x` leave the logs in
  place. A project reset between verification rounds does not erase earlier evidence.
- The Unity side rotates a file past 10 MB and keeps the newest 20 `unity_vibe_*` files
  (`VibeLoggerService` in `Packages/src/Editor/ToolContracts/VibeLogger.cs`).

## When they exist

- Unity side: only when the `ULOOP_DEBUG` scripting define symbol is set in the project. Every
  log method is `[Conditional("ULOOP_DEBUG")]`, so without it nothing is written — not even
  errors. This repository's development project defines it for the Standalone build target.
- CLI side: only when the `ULOOP_DEBUG` environment variable is set to a value other than empty,
  `0`, or `false` (`cli/common/vibelog/cli_vibe.go`).
- A missing line is evidence only when the define was set and the code path logs at all.
  Coverage is per call site, not per command: for example, `execute-dynamic-code`,
  `simulate-keyboard`, `screenshot`, compile, domain reload, server binding, and hot reload log
  start and completion, while the CLI side currently logs compile requests and window focus only.
  Check that the operation you expect is logged somewhere (`git grep` the operation name) before
  reading its absence as "it never ran".

## Entry format

Each line is one JSON object:

```json
{"timestamp":"2026-01-01T12:00:00.000+09:00","level":"INFO","operation":"execute_dynamic_code_start","message":"...","context":{"correlationId":"<ID>","...":"..."},"correlation_id":"unity_<ID>_<HHMMSS>","source":"Unity","stack_trace":null,"environment":{"domain_reload_state":"Idle"}}
```

- `operation` is the stable key to filter on. Count operations first to see what the file covers.
- Pair start and completion entries by the ID the call site puts in `context` (for
  `execute-dynamic-code` it is `context.correlationId`), not by the top-level `correlation_id`,
  which is generated per log call.
- `environment.domain_reload_state` tells whether an entry was written during a domain reload.

## How to use them in an investigation

1. Find the time window from the report or the command output, then open the matching
   `unity_vibe_*.json` (remember the UTC file date).
2. Count `operation` values in the window. A `*_start` with no matching `*_complete` is the
   first lead behind a BUSY error: the Editor is single-flight, so while one request never
   finishes, every later command is rejected with BUSY after the CLI's bounded retry. Before
   treating the missing completion as proof, check the next UTC day's file and confirm that the
   operation logs its completion at all.
3. Read the entries around the orphan in time order: the last operation logged for its ID shows
   how far it got, and neighboring `domain_reload_*`, `startup_protection_active`, or
   `binding_*` entries show what the server was doing at that moment.
4. Line the Unity entries up against `cli_vibe_*.json` for the same window when the question is
   whether a request reached the Editor at all.

Report what the log shows and what it cannot show (for example, "the request stalled after the
executor was created; which await it was blocked on is not logged") instead of "logs were not
collected".
