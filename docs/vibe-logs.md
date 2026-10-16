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
  Hot reload also writes one `hot_reload_timing_detail` per apply run, with the milliseconds of
  each step outside the response's `Timing` phases (`steps`), the phases themselves, and
  `unaccountedMs` — the part of `otherMs` no step covers. A negative `unaccountedMs` means a
  step overlaps a phase.
  When the end of a run evicted entries from the call-site cache, it also writes one
  `hot_reload_call_site_cache_evicted` with the evicted count and bytes, the bytes still cached,
  and the budget — a sign that the assemblies one run scans do not fit the cache and are read
  again on the next run.
  The caller-note resolution of a run (the `LifecycleNote` that says a patched method is called
  only from one-shot lifecycle messages) reads at most one compiled assembly that is not in the
  call-site cache yet; when it would need more, the note is omitted and the run writes one
  `hot_reload_caller_note_load_budget_exhausted` with `budgetLoads` and the `refusedAssemblies`
  it did not read. Right after the run the refused assemblies are read on a pool thread; then the
  notes are recomputed without reading anything, and the assemblies that recompute would still
  refuse (callers one level further, found only once the refused ones are read) are read too,
  for at most four recomputes. When that finishes the run's correlation id gets one
  `hot_reload_caller_note_backfill_complete` with `requested` (distinct dlls tried), `loaded`,
  `failed` (dlls that could not be read; the next run reads them itself), `rounds` (recomputes
  done), `totalMs` and `cancelledBy` (`run` when the next run came first,
  `beforeAssemblyReload`, `compilationStarted`, `services_replaced`, or null). The recompute
  writes no `hot_reload_caller_note_load_budget_exhausted`. The next run finds them cached, so a
  note that needs more than one assembly appears from the second run on; a run's
  `warm_up_yield` step also covers the wait for a backfill's dll in flight.
  The time inside the analysis and shim-compile phases is split by separate entries, never by
  `steps`, so `unaccountedMs` keeps its meaning. `hot_reload_worker_request_timing` (one per
  request to the transform worker, prepare and transform) has the gate wait, launch target
  resolution, input write, process spawn, response wait and output read, whether the request
  started the worker, and the stages the worker timed itself (`workerSteps`; empty when no
  output decided the request, such as a non-zero exit code, but kept for a request that fails on
  parse errors). `hot_reload_signature_gate_timing` (one per group that reaches the signature
  gate) has the main-thread switch and the gate. `hot_reload_shim_first_compile_timing` (one per
  group whose first-pass shim is compiled; none when the gate's retry already compiled it, when
  the membership check fails, or when there is no method to compile) has the main-thread switch,
  the membership check and the shim compile.
  `hot_reload_shim_references_timing` (one per shim compile) has the resolver directories, the
  target's publicized copy, the artifact copies and the reference copies with their counts.
  `hot_reload_publicized_copy_written` appears only when a rewritten copy was written, with
  its rewrite and write time. The warm-up's `publicized_targets` item writes it too, before any
  run. `hot_reload_source_snapshot_checked` appears when a capture checked sources written
  since the compile started against the PDB: `suspect` sources checked, `editedAfterCompile`
  of them marked as not the compiled source, and `checkMs`.
  `hot_reload_shim_compiler_timing` has the compile, the load and
  the backend kind. `dynamic_code_shared_worker_started` appears whenever no live shared Roslyn
  worker could serve the compile and one had to be started, with its source sync, worker
  assembly check and process spawn; `ready` is false when the start failed (no worker
  assembly, or the process did not start). `workerAssemblySource` says where the worker
  assembly came from: `existing` (already in this Editor's worker directory), `cache` (copied
  from `<OS temp>/UnityCliLoopCompilation/RoslynWorkerCache/<key>/`, keyed by the worker source
  and the compiler paths), or `built` (csc ran); `cachePublish` and `cachePublishError` say
  whether a built assembly was added to that cache.
  After each server reset, the shared-worker warm-up writes one
  `dynamic_code_shared_worker_warm_up_skipped` with its `reason` (`no_hot_reload`: the hot-reload
  tool has not initialized in this domain; `no_targets`: `targets.txt` names no assembly whose dll
  exists and whose compilation assembly Unity lists; `compiler_unavailable`: the external compiler
  is missing, which only the next real compile reports; `stopped_for_tests`), or one
  `dynamic_code_shared_worker_warm_up_complete` with `ms` (the whole warm-up), `referencesMs`
  (listing the references), `mainThreadMs` (reading the package path on the main thread),
  `warmMs` (starting the worker and its compile), `referenceCount`, `outcome`, `failureReason`
  and `errorCount`. `outcome` is `answered` (the worker answered the compile; `errorCount` is the
  errors it reported), `already_running` (a compile had started the worker first, so nothing was
  sent), `start_failed` or `request_failed` (with `failureReason`; the worker is stopped and the
  next compile starts it again), or `failed` (an exception, with `failureReason` set to its type
  and `exceptionMessage`, logged as a warning).
  The source snapshot capture reads its inputs on the main thread and then runs on a pool
  thread, one capture at a time. Each start writes `hot_reload_source_snapshot_capture_started`
  with its `trigger` and `mainThreadMs` (the time the main thread spent reading the inputs and
  handing the capture over). How it ended follows as one of these, with the same `trigger`:
  - `hot_reload_source_snapshot_captured`, once per domain, with `captureMs` (the time on the
    pool thread). `trigger` is `domain_load` normally; `first_update_tick`, `apply` or
    `pause_point` when the capture at domain load failed, found no compiled dll with a PDB,
    or was cancelled, or when the captures at startup were skipped and a hot reload run or a
    pause point is the first to read the snapshot.
  - `hot_reload_source_snapshot_capture_failed` (a warning) with `error` (the exception type) and
    `message`.
  - `hot_reload_source_snapshot_capture_empty` when no compiled dll with a PDB beside it was
    found.
  - `hot_reload_source_snapshot_capture_cancelled` when a domain reload or a compile start
    stopped it.
  After any of the last three, the next reader or the first update tick starts a new capture.
  While a capture runs, `get-compile-status` reports not ready, so `uloop compile` returns only
  after it. That hold gives up after 30 seconds and writes one
  `hot_reload_source_snapshot_capture_hold_cap_exceeded` (a warning). Readers keep waiting for
  the capture. `compile_status_query_received` carries `is_snapshot_capture_holding`. When both
  the hot-reload and pause-point tools are disabled in the tool settings, the captures at `domain_load` and
  `first_update_tick` each write `hot_reload_source_snapshot_skipped` with that `trigger` and
  `reason` `tools_disabled` instead, and the first hot reload run or pause point after either
  tool is enabled captures it.
  `hot_reload_source_snapshot_breakdown` appears once each time the capture runs to the end,
  just before `hot_reload_source_snapshot_captured`. It also appears when no compiled dll with a
  PDB beside it was found, in which case `hot_reload_source_snapshot_capture_empty` follows
  instead, and it does not appear when the capture threw or was cancelled. The capture lists the
  compiled assemblies from the dlls and PDBs in the compiled assemblies folder and reads an
  assembly's sources from its PDB, not from Unity's compilation assembly list. It says where the capture spent its time:
  - `listingMs`: listing the compiled dlls that have a PDB beside them.
  - `pdbSourcesMs`: reading the sources of each assembly whose dll changed from its PDB.
  - `packageLookupMs`: reading whether each assembly's sources are immutable, which the PDB's
    sources and the package roots read once from Package Manager decide, so this is close to 0;
    it no longer asks Package Manager for each assembly.
  - `stampMs`: reading and writing the stamps and the read-only markers beside the snapshots, and
    reading the previous build's manifest.
  - `mvidMs`: reading the MVID of each compiled dll whose stamp did not match.
  - `statMs`: reading each source's length and write time before and after it is copied, and
    checking whether a source and its previous copy are unchanged.
  - `readWriteMs`: reading each source and writing its copy, including deleting the previous copy
    it replaces.
  - `pdbCheckMs`: checking the sources written since the compile started against the PDB.
  - `publishMs`: deleting a leftover temporary directory and creating a new one or moving the
    previous build's directory there, writing the manifest, and moving the directory into place.
  - `cleanupMs`: deleting the snapshot directories of earlier builds, and the copies of sources
    the assembly no longer lists.
  - `totalMs`: the whole capture. Reading the compile start and the package roots before it is
    not counted here.
  - `assemblies`: the compiled assemblies listed.
  - `assembliesImmutable`: those skipped because their sources belong to an immutable package:
    either their read-only marker matched the compiled dll, or their PDB showed it and the marker
    was written.
  - `assembliesUnchanged`: those skipped because their stamp matched the compiled dll.
  - `assembliesCaptured`: those whose sources were copied into a new snapshot directory.
  - `assembliesSkippedPdbMismatch`: those skipped because their PDB belonged to another build of
    the dll, as while a compile replaces them; no stamp is written, so the next domain captures them.
  - `assembliesFromCompilerPdb`: assemblies whose sources were read from the compiler's own PDB
    under `Library/Bee` because the PDB beside the dll had lost documents.
  - `filesReused`: the sources whose previous copy was kept without reading them.
  - `filesCopied` and `bytesCopied`: the sources copied and their total size.
  - `filesChecked`: the sources checked against the PDB.
  The phases add up to `totalMs` except for the loop overhead; `captureMs` in the captured entry
  also includes reading the compile start and the package roots, and writing this breakdown entry.
  `hot_reload_compilation_assemblies_fetched` appears each time hot reload asks Unity for the
  compilation assembly list, with `fetchMs` and `assemblyCount`. After a domain reload the list is
  normally rebuilt off the main thread from the list a previous domain saved
  (`hot_reload_compilation_list_reconstructed` below), so this appears only when that rebuild kept
  no list or the cache was invalidated in the domain, from the first hot reload run that needs the
  list. The list is then kept for the rest of the domain once it is not empty.
  `hot_reload_compilation_list_reconstructed` appears once per domain reload. `filled` says whether
  the rebuilt list filled the cache, and `reason` says why it did not:
  - `no_saved_list`: no list has been saved yet.
  - `saved_list_unreadable`: the saved list is torn, of another format version, or missing a
    required field.
  - `assembly_set_changed`: an assembly definition was added or removed after the list was saved.
  - `pdb_mismatch`: the PDB beside a compiled-again dll belongs to another build, as while a
    compile replaces the files.
  - `pdb_documents_incomplete`: neither the PDB of a compiled-again assembly nor the compiler's
    own PDB of the same build shows that it names every source the compile was given.
  - `unresolved_reference`: a reference has a file name the saved list does not know, or a project
    reference is not in the list.
  - `reference_missing`: every path saved for a reference's file name is gone, as when a package
    update renames its folder.
  - `reference_changed`: a saved path exists, but not with the dll of the recorded MVID.
  - `compilation_failed`: the last compile failed, so the dlls are older than the sources.
  - `superseded`: the cache was filled or invalidated meanwhile.
  - `cancelled`: a compile started or the domain is ending.
  - `tool_disabled`: the hot-reload tool is disabled in the tool settings.
  - Any other value is the type name of the exception that stopped the rebuild.
  `reconstructMs` is the time from the start of the rebuild to the entry, including the wait for
  the main thread. `backgroundMs` is the time spent rebuilding on a pool thread, without the wait
  for that thread, and `mainThreadWaitMs` the time it then waited for the main thread to run again.
  Either is null when the rebuild did not get through that step. A `tool_disabled` entry has only
  `filled` and `reason`, because no rebuild started.
  When the cache was not filled, the first hot reload run that needs the list asks Unity for it
  (`hot_reload_compilation_assemblies_fetched`) and saves it when the cache was not invalidated
  in the domain and the last compile did not fail, so a reason that comes from an old saved list
  (`no_saved_list`, `saved_list_unreadable`, `assembly_set_changed`, `unresolved_reference`,
  `reference_missing`, `reference_changed`) does not repeat after that. Without a list, the
  warm-up skips `publicized_targets`, `referenced_method_sets` and `transform_worker` with
  `compilation_list_unavailable`.
  `hot_reload_compilation_list_saved` appears when a list was written for the next domain, with
  its size in `bytes`, `saveMs`, and the counts of `assemblies` and `references`. It is written on
  a pool thread after a list is asked of Unity and after a rebuilt list fills the cache, only when
  the cache was not invalidated in the domain and the last compile did not fail.
  `hot_reload_compilation_list_save_skipped` names why a list was not saved in `reason`:
  `generation` (the cache was invalidated in the domain), `compilation_failed`, or `superseded`
  (the cache was invalidated while it was written, as when a compile started).
  `hot_reload_compilation_list_save_failed` is a warning with the exception type name in `error`.
  After a domain reload, the warm-up writes one `hot_reload_warm_up_complete` with the target
  assemblies, each item's `outcome` (`done`, `cancelled` or `failed`) and time, and `cancelledBy`
  (`run`, `beforeAssemblyReload`, `compilationStarted`, or null), or one
  `hot_reload_warm_up_skipped` with its `reason` (`no_targets`, `compiling`, `updating`,
  `no_compiled_assembly`, `run_started_first`, `tool_disabled` when the hot-reload tool is
  disabled in the tool settings). `cancelled` covers both an item that never started
  (`ms` 0) and one stopped between its units (one dll or one transform worker request each) with
  units left, with the time it ran. The items run in the order `publicized_targets`, `call_sites`,
  `referenced_method_sets`, `transform_worker`, `pdb_documents`. The warm-up's transform worker
  requests write `hot_reload_worker_request_timing` too: the ones between a domain reload and its
  `hot_reload_warm_up_complete` are the warm-up's.
  A run's `steps` have `warm_up_yield`, the time it waited for the unit in flight, and `warm_up_targets`, the time it took to record its
  assemblies for the next warm-up.
- CLI side: only when the `ULOOP_DEBUG` environment variable is set to a value other than empty,
  `0`, or `false` (`cli/common/vibelog/cli_vibe.go`).
- A missing line is evidence only when the define was set and the code path logs at all.
  Coverage is per call site, not per command: for example, `execute-dynamic-code`,
  `simulate-keyboard`, `screenshot`, compile, domain reload, server binding, and hot reload log
  start and completion, while the CLI side logs compile requests, window focus, the tool
  requests described under "CLI entries for tool commands" below, and `compile-check` runs
  ("CLI entries for compile-check").
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
| `cli_tool_request_failed` (`ERROR`) | when no answer came, or Unity answered with an error | `command`, `correlation_id`, `elapsed_ms`, `request_accepted`, `error_kind` (`rpc:<error data type>`, `final_response_timeout`, or `other`), `running_tool_name` (only when `error_kind` is `rpc:server_busy` and Unity named the tool that held the Editor) |
| `cli_hot_reload_busy_wait_decided` | when the first `hot-reload` answer is `server_busy` (another uloop command holds the Editor; the preceding `cli_tool_request_failed` has `error_kind` `rpc:server_busy`) | `correlation_id` (the first request's), `running_tool_name`, `running_tool_phase`, `running_tool_elapsed_seconds`, `budget_ms`, `resend_interval_ms` |
| `cli_hot_reload_busy_wait_complete` (`WARN` unless the Editor became ready and the request sent after the wait answered) | after the wait and the one request sent after it, on every way out of a wait that started (a cancel, a failed send, and a bad answer included) | `correlation_id` (the first request's), `second_correlation_id` (the request sent after the wait, or empty when none was sent), `waited_ms`, `ready`, `resends` (requests sent again during the wait while a cancelled `execute-dynamic-code` held the Editor; each also has its own `cli_tool_request_sent`), `resend_correlation_ids` (the `correlation_id` of every request sent again during the wait, in order; its length is `resends`, and its last entry equals `second_correlation_id` when a resent request got in), `second_result`, and that answer's `second_success` and `second_outcome` |
| `cli_hot_reload_editor_ready_retry_decided` | after every `hot-reload` answer (the one after a busy wait, when one ran), before the fallback decision | `correlation_id` (the request's), `requested`, `parse_error` |
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

## CLI entries for compile-check

`compile-check` never talks to the Editor, so these two entries are the only record of what a run
did. Both carry the same `correlation_id`. A run that cannot find its project writes neither,
because there is no project to write into.

| `operation` | Written | `context` |
|---|---|---|
| `cli_compile_check_start` | once the project is found | `command`, `all`, `full`, `jobs`, `project_identity` |
| `cli_compile_check_complete` (`INFO`; `WARNING` when the run found compile errors; `ERROR` when it could not finish) | on every way out of a run that wrote the start entry | `command`, `outcome` (`succeeded`, `errors`, or `failed`), `duration_ms`; for `failed`, `error` and `error_code`, and `build_settings` when the run got as far as comparing the build settings; otherwise `error_count`, `warning_count`, `compiled`, `reused`, `fast_checked`, `blocked` (assembly names, as in the response), `skipped_count`, `fast_checks` (`assembly`, `reused`, `changed_file_count`), `fallbacks` and `baseline_notes` (`assembly`, `reason`), `units` (`assembly`, `duration_ms`, `succeeded` of each assembly the run compiled or checked; replayed ones are left out; `assumed_kept` names the references it started ahead of, assuming they kept Unity's surface, `compiled_again_after` the one whose changed surface made it compile again and `discarded_ms` how long the compile it threw away took, `waited_for_record` says it waited for its references for a record that then did not apply, and `changed_by` lists the references whose changed surface counted for it (`reference`, and `name`, the first changed name it writes, empty when no name tells the change, and `reason` when a change told by names counted without a name it writes: `no saved generator output`) - each only when it applies), and `surface_checks` (`assembly`, `verdict`, `detail`, `duration_ms`, `from_record` of each assembly whose rebuilt reference assembly differed in bytes from Unity's and was compared by the worker or taken from its record, replayed ones included; `changed_name_count` and `changed_names`, the first 10 of the identifiers a dependent has to write to be affected by the change, and `unnamed`, why no name tells the change, only when it applies), `name_skips` (`assembly`, `unaffected_by`: the assemblies left out although a reference changed, because they write none of the names the change touches; only when there is any), and `name_index` (`duration_ms`, `units`, `with_generated`, `without_generated` (the assemblies compiled on a named change because they have analyzers and no saved generator output), `unreadable`, `sources_read`: what reading the names of the assemblies with references in the plan took, and whether the run read their sources - it does only once it compiles an assembly, has the worker check changed files, or a verdict needs the names; only when the plan has an assembly with references), and `build_settings` (`stamp_found`: a build-trace file of the last Unity build exists; `record`: `new` when the run recorded the settings of a build for the first time, `replaced` when it recorded them after another Unity build, `kept` when it compared them against the record of the same build, empty when there was no build-trace file; `refused_part`: the setting a refusal was for, empty otherwise; `defines_checked`: the run looked for a scripting define the build lacks, which it does only when `ProjectSettings.asset` was saved after the last build or there is no build-trace file) |

- Unlike the tool-command entries, the complete entry holds the run's error message and fallback
  reasons: they can name assemblies and files of the project, which the command's own output prints
  as well.

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
