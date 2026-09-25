# Pause Point `--line` Is a Line of the Edited File

Date: 2026-09-25

## Decision

`enable-pause-point --file <path> --line <N>` always reads `N` as a line of the file as it is
on disk now, whether or not hot reload has run.

- A line inside a hot-reload patched method's edited body arms the patched body directly
  (the shim path, unchanged by this decision).
- Any other line is mapped onto the verified source snapshot of the last compile: the
  compile-time source whose bytes match the portable-PDB document checksum. The map is a
  line diff between that snapshot and the edited file, built in pure C# on the pause-point
  side. The compiled statement the mapped line resolves to is armed, and the response
  reports the edited line in `ResolvedLine` / `ResolvedLineText` with
  `LineBasis: EditedFile`.
- A line that has no counterpart in the snapshot is refused with
  `PAUSE_POINT_LINE_NOT_COMPILED`. So is a line whose forward rounding would cross an
  uncompiled statement, or one that resolves to a compiled statement no longer at that place.
  A line inside a method hot reload added is refused with `PAUSE_POINT_RESOLVE_FAILED`;
  `--method` no longer arms a compiled method there.
- A line that rounds onto the compiled body of a patched method is refused with
  `PAUSE_POINT_PATCHED_BY_HOT_RELOAD`, naming that method's edited line range. So is a line
  whose forward rounding would cross an uncompiled statement inside a patched method's edited
  body: that method is already patched, so another hot reload would return the same refusal.
- A line with no compiled statement on or after it is refused with
  `PAUSE_POINT_RESOLVE_FAILED`. The message names the requested line and lists the nearby
  compiled methods' spans in edited-file lines, and the next action asks for a compile only
  if the wanted statement was added after the last compile. When `--method` was passed, the
  next action first asks to check it: a name that matches no method in the file fails on every
  line. A post-line request on a statement that always throws names that statement's edited
  line.
- Only when no verified snapshot exists for the file is `N` used as a compiled line number,
  reported as `LineBasis: LastCompiledSource` with one warning.

## Context

Before this decision, `--line` on a method hot reload had not patched was resolved against
the last compiled PDB, while the caller's line numbers came from the edited file. After an
edit shifted lines, forward rounding over the whole file could arm a different statement or
a different method without an error. Each symptom got its own remedy: a text-matching remap,
candidate line hints, drift comparison warnings, a "pass `--method`" suggestion, and a line
shift warning on the hot reload response. These parts were written independently, and their
warnings and recommended next actions began to contradict each other.

Across twenty-one usability rounds, 69 findings were adopted; 7 were about pause point line
resolution or drift, and all 7 came in the last four rounds. In the last of them, one of four
fix pull requests was put on hold because its guidance conflicted with another part. Adding
more guidance was not converging.

The verified source snapshot already existed for hot reload. Because it is checked against
the PDB document checksum, its line numbers match the PDB one to one, so a line diff against
it translates edited lines into compiled lines without any help from the compiler.

## Rejected Alternatives

- **The hot reload worker generates an edited-to-compiled line table per method and passes
  it to pause point through a DTO and a port.** This crosses the worker boundary (metadata
  names, DTOs, protocol) and every patch path would have to guarantee the table. The snapshot
  gives the same map on the pause-point side, testable without Unity.
- **Keep last-compiled coordinates and keep explaining the gap in guidance text** (candidate
  lines, `--method` suggestions, line map warnings). This is the approach that stopped
  converging; every new case added another guidance part that had to agree with the others.

## Consequences

- Guidance no longer mentions compiled line numbers except for the no-snapshot fallback. The
  remap, candidate, drift comparison and line shift warning code and their tests are gone.
- Lines are compared after `Trim()`. A line whose only change is a trailing comment is
  treated as changed and refused. This errs on the safe side and is accepted.
- An edited but not hot-reloaded line can no longer be armed at all; the caller hot-reloads
  or compiles first, or picks an unchanged line.
- `LineBasis` is `EditedFile` in every case except the fallback, so a caller can tell from one
  field whether `ResolvedLine` matches the editor.

## Reversal condition

Reopen this decision if the fallback becomes the common case because verified snapshots are
often unavailable in real projects, or if hot reload gains PDBs with sequence points for
every patch so that the snapshot map and the shim path can be merged into one resolver.
