# Claude Code Sandbox and Unity IPC

Read this when a `uloop` command fails against a running, healthy Unity Editor while an AI agent
(Claude Code or similar) is executing it through a sandboxed shell. The symptom looks like an IPC
bug and has already cost one full investigation (2026-07-26) that ended in "the Editor was fine
all along" — this document exists so nobody walks that path again.

## Symptom

- Any `uloop` command that talks to the Unity Editor (`compile`, `run-tests`, `get-logs`,
  `simulate-*`, ...) fails, while Unity itself is demonstrably healthy and the server side never
  sees the connection attempt (server-side logs are VibeLogger-based and exist only when the
  `ULOOP_DEBUG` scripting define is set — do not read missing log lines as evidence either way).
- Commands that never touch the Editor (`uloop --version`, `uloop --help`) work normally.
- The reported error names the refusal: the command fails on the first attempt with
  `ErrorCode: UNITY_NOT_REACHABLE`, `Retryable: false`, `SafeToRetry: false`, and
  `Details.Cause` carrying the syscall error verbatim
  (`dial unix ...: connect: operation not permitted`). Its next actions point at sandboxing and
  socket permissions, not at waiting. An older CLI instead retried for 60 seconds and then
  reported `dial unix ...: i/o timeout` with retry guidance — that misdiagnosis is what cost the
  2026-07-26 investigation, so an `i/o timeout` here means the CLI predates the fix.

## Cause

Claude Code runs shell commands inside a sandbox whose network policy is expressed as a list of
allowed **hostnames**. A Unix domain socket has no hostname, so there is no way to allowlist the
project socket (`/tmp/uloop-<euid>/UnityCliLoop-<hash>.sock`, where `<hash>` is the first 16 hex
digits of the SHA-256 of the canonical project root) through that policy — `connect()` and
`bind()` on Unix sockets are denied with EPERM regardless of filesystem permissions. Write
access to the socket's directory does not help; this was verified empirically: a directory the
sandbox allowed file writes into still refused a socket `bind()`.

The block is not specific to the transport: with the default `allowedHosts` policy the sandbox
also stops V2's localhost TCP connection (verified 2026-07-27), so a plain `uloop ...` command
succeeding proves only that `excludedCommands` took it out of the sandbox — not that Unix sockets
are the problem and TCP would get through. What is specific to a Unix socket is only that it has
no hostname to put on `allowedHosts`, so that escape hatch does not exist for it.

This is specific to the sandboxed shell. The same command in a normal terminal, or in a session
without sandboxing, is unaffected. Windows uses a named pipe instead of a Unix socket; whether
the sandbox blocks named-pipe connects the same way has not been verified — treat an
EPERM-shaped failure there with the same suspicion before blaming the Editor.

## Why this repository gets hit harder than game projects

Claude Code's sandbox supports an `excludedCommands` list (personal `settings.json`), and a
typical entry is `"uloop *"`. That pattern matches the **command text**, with these verified
consequences (2026-07-26, all measured in a live sandboxed session):

| Invocation | Matches `uloop *` | Result |
|---|---|---|
| `uloop get-logs ...` (dispatcher from PATH) | yes — runs outside the sandbox | works |
| `SOME_VAR=... uloop ...` (env-var prefix) | yes (verified empirically) | works |
| `ULOOP_PROJECT_RUNNER_PATH=<dist runner> uloop ...` (or `export` first, then plain `uloop ...`) | yes — the command text still starts with `uloop` | works |
| `dist/darwin-arm64/uloop compile ...` | **no** (verified with the plain literal path) | EPERM |
| raw `socket.connect()` from a script | no | EPERM |

The exclusion is decided on the **typed command text**, not on which binary ultimately does the
work: the `ULOOP_PROJECT_RUNNER_PATH` row runs a locally built dev runner yet stays excluded,
while the `dist/...` row runs the same kind of dispatcher binary yet gets sandboxed. Game
projects invoke plain `uloop ...` (optionally with the env override) and never notice the
sandbox. This repository's development rule (see `CLAUDE.md` — always validate with the built
`dist/<platform>/uloop` binary) produces exactly the command shape the exclusion does
**not** match.

Note the corollary: a successful `uloop ...` command in a sandboxed session does not mean the
sandbox permits Unity IPC — it means the command was excluded from sandboxing entirely.

## Remedies

Pick one:

1. Run dev-binary commands with the sandbox disabled for that command (Claude Code:
   `dangerouslyDisableSandbox`; users can manage restrictions via `/sandbox`).
2. Add the dev-binary shapes to `excludedCommands` in the personal Claude Code settings:
   `"dist/*/uloop *"` alongside the existing `"uloop *"`, plus an anchored absolute-path entry
   such as `"/Users/<user>/ghq/<org>/*/dist/*/uloop *"` if you ever type the binary's full path.
   Both are measured, not suggestions (2026-07-27). One command shape still defeats them; see
   the next section before relying on this remedy.
3. When the change under review lives in the project runner, keep the sandbox on and run
   `ULOOP_PROJECT_RUNNER_PATH=<absolute path to the dist runner> uloop ...` — the plain-`uloop`
   command text stays excluded while the dev runner does the work (the override is documented in
   `docs/project-runner-pin.md`). This does not exercise dispatcher-side changes; for those, use
   remedy 1 or 2.

Do not burn time re-investigating the Editor side when the error is EPERM: the Editor never
saw the connection attempt.

## Which command shapes the exclusion actually covers

Measured 2026-07-27 in a live sandboxed session. Each row was decided by whether the command
reached Unity or failed at `connect()`. The entries in play were `"uloop *"`, `"dist/*/uloop *"`,
and an anchored absolute-path entry (`"/Users/<user>/ghq/<org>/*/dist/*/uloop *"`).

| Command as typed | Excluded |
|---|---|
| `dist/darwin-arm64/uloop get-logs ...` | yes |
| `/Users/<user>/.../dist/darwin-arm64/uloop get-logs ...` (absolute) | yes, via the anchored entry |
| `SOME_VAR=abc dist/darwin-arm64/uloop get-logs ...` (literal env value) | yes |
| `dist/darwin-arm64/uloop get-logs --project-path "$(git rev-parse --show-toplevel)"` | yes |
| `P=/path; dist/darwin-arm64/uloop get-logs --project-path "$P"` (variable in an argument) | yes |
| `mkdir -p somewhere; dist/darwin-arm64/uloop get-logs ...` (compound) | yes |
| `echo start; uloop get-logs ...` (compound) | yes |
| `P=/path; uloop get-logs --project-path "$P"` | yes |
| **`V=abc; SOME_VAR="$V" dist/darwin-arm64/uloop get-logs ...`** | **no — denied** |
| **`V=abc; SOME_VAR="$V" /Users/<user>/.../dist/darwin-arm64/uloop get-logs ...`** | **no — denied** |
| **`V=abc; SOME_VAR="$V" uloop get-logs ...`** | **no — denied** |

Command substitution in an argument, a variable in an argument, and being part of a compound
command are all harmless. Two things are worth knowing:

- **An absolute path needs its own entry.** The glob is matched against the typed text, so
  `dist/*/uloop *` only matches text beginning with `dist/`. Adding an entry whose leading
  segment is literal — `"/Users/<user>/ghq/<org>/*/dist/*/uloop *"` — makes the absolute form
  work, verified above. Do not reach for a leading wildcard such as `"*/dist/*/uloop *"`: every
  other entry is anchored at the front, and an unanchored one would let an arbitrary command
  prefix ride into the exclusion.
- **A shell variable expanded into an env-var prefix defeats every entry.** The last three rows
  differ from working ones only in that the env value is `"$V"` rather than a literal, and they
  fail for the relative dev binary, the absolute dev binary, and the installed `uloop` alike.
  The same variable expanded into an *argument* is fine. This is not specific to any variable
  name or to `uloop`, so it is a property of Claude Code's exclusion matching rather than
  something this repository can fix; the practical rule is to write env-prefix values literally.
  `ULOOP_PROJECT_RUNNER_PATH="$RUNNER" uloop ...` (remedy 3 above) is exactly the shape to avoid.

The last row also reproduces the misdiagnosis this document warns about. Because the installed
`uloop` resolves a *released* project runner, its failure is not the refusal report above but
`... i/o timeout` after the retry window — the pre-fix behaviour described under Symptom. Seeing
`i/o timeout` therefore still means the runner that served the command predates the fix, not
that the Editor is slow.
