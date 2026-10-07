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
  start and completion, while the CLI side logs compile requests, window focus, and the tool
  requests described under "CLI entries for tool commands" below.
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

## CLI entries for tool commands

A tool command that sends one request and prints Unity's answer, such as `hot-reload` or
`get-logs`, writes these entries. A command that runs a wait or a flow of its own writes none of
them: `compile` (it has its own `cli_compile_*` entries), an `execute-dynamic-code`, `run-tests`,
or `control-play-mode` that waits for a domain reload or a Play Mode change, and commands such as
`enable-pause-point`, `await-pause-point`, and `status`.

| `operation` | Written | `context` |
|---|---|---|
| `cli_tool_request_sent` | before the request is sent | `command`, `correlation_id`, `project_identity`, `cli_version`, `param_keys` (sorted), `array_lengths` (element count of each array parameter) |
| `cli_tool_response_received` | when Unity answered | `command`, `correlation_id`, `elapsed_ms`, `request_accepted`, `result_bytes`, `exit_code` |
| `cli_tool_request_failed` (`ERROR`) | when no answer came, or Unity answered with an error | `command`, `correlation_id`, `elapsed_ms`, `request_accepted`, `error_kind` (`rpc:<error data type>`, `final_response_timeout`, or `other`) |
| `cli_hot_reload_editor_ready_retry_decided` | after every `hot-reload` answer, before the fallback decision | `correlation_id` (the request's), `requested`, `parse_error` |
| `cli_hot_reload_editor_ready_retry_complete` (`WARN` unless the Editor settled and the second apply answered) | after the wait and the second apply, when a retry was requested | `correlation_id` (the first request's), `second_correlation_id` (the second request's, or empty when none was sent), `waited_ms`, `ready`, `second_result`, and the second answer's `second_success` and `second_outcome` |
| `cli_hot_reload_compile_fallback_decided` | after every `hot-reload` answer the fallback decision sees: the second one after an editor-ready retry, and none when that retry ended the command (see `cli_hot_reload_editor_ready_retry_complete`) | `correlation_id` (the request's), `requested`, `parse_error`, and the answer's `success`, `outcome`, `warnings_count`, and `timing` (numbers only) |
| `cli_hot_reload_compile_fallback_complete` (`ERROR` unless `succeeded`) | after the fallback compile, when one ran | `correlation_id`, `elapsed_ms`, `compile_exit_code`, `compile_result_bytes`, `merged`, `succeeded` |

- These entries hold keys, counts, sizes, and flags, never a parameter value, a response body,
  or an error message: a parameter can name the project's files or carry code, and Unity's
  parameter-validation error quotes the values it rejected. Read the values from the command's
  own output.
- `cli_tool_response_received` with `exit_code` 1 is a tool that answered with a failure; the
  request itself got through.
- The `cli_tool_*` entries of one command share one `correlation_id`, and the two
  `cli_hot_reload_*` entries share another. Pair the two groups by time.

## How to use them in an investigation

1. Find the time window from the report or the command output, then open the matching
   `unity_vibe_*.json` (remember the UTC file date).
2. Count `operation` values in the window. A `*_start` with no matching `*_complete` is the
   first lead behind a BUSY error: the Editor is single-flight, so while one request never
   finishes, every later command is rejected with BUSY after the CLI's bounded retry. Before
   treating the missing completion as proof, check the next UTC day's file and confirm that the
   operation logs its completion at all. A `tool_execution_lease_revoked` entry means the
   Editor stopped waiting for a cancelled `execute-dynamic-code` request that had not finished
   and let other commands run; that request is the one that was stuck.
3. Read the entries around the orphan in time order: the last operation logged for its ID shows
   how far it got, and neighboring `domain_reload_*`, `startup_protection_active`, or
   `binding_*` entries show what the server was doing at that moment.
4. Line the Unity entries up against `cli_vibe_*.json` for the same window when the question is
   whether a request reached the Editor at all.

Report what the log shows and what it cannot show (for example, "the request stalled after the
executor was created; which await it was blocked on is not logged") instead of "logs were not
collected".
