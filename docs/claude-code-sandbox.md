# Claude Code Sandbox and Unity IPC

Read this when a `uloop` command fails against a running, healthy Unity Editor while an AI agent
(Claude Code or similar) is executing it through a sandboxed shell. The symptom looks like an IPC
bug and has already cost one full investigation (2026-07-26) that ended in "the Editor was fine
all along" — this document exists so nobody walks that path again.

## Symptom

- Any `uloop` command that talks to the Unity Editor (`compile`, `run-tests`, `get-logs`,
  `simulate-*`, ...) fails, while Unity itself is demonstrably healthy and the server-side log
  shows a successful bind and then silence.
- Commands that never touch the Editor (`uloop --version`, `--help` rendering) work normally.
- The reported error is misleading: the CLI retries for 60 seconds and then reports
  `dial unix ...: i/o timeout`, while the underlying per-attempt error is
  `connect: operation not permitted` (EPERM). Fixing that misdiagnosis is tracked as its own
  work item (report the permanent errno verbatim and stop advising retries).

## Cause

Claude Code runs shell commands inside a sandbox whose network policy is expressed as a list of
allowed **hostnames**. A Unix domain socket has no hostname, so there is no way to allowlist the
project socket (`/tmp/uloop-<uid>/<project>.sock`) through that policy — `connect()` and
`bind()` on Unix sockets are denied with EPERM regardless of filesystem permissions. Write
access to the socket's directory does not help; this was verified empirically: a directory the
sandbox allowed file writes into still refused a socket `bind()`.

This is specific to the sandboxed shell. The same command in a normal terminal, or in a session
without sandboxing, is unaffected.

## Why this repository gets hit harder than game projects

Claude Code's sandbox supports an `excludedCommands` list (personal `settings.json`), and a
typical entry is `"uloop *"`. That pattern matches the **command text**, with these verified
consequences (2026-07-26, all measured in a live sandboxed session):

| Invocation | Matches `uloop *` | Result |
|---|---|---|
| `uloop get-logs ...` (dispatcher from PATH) | yes — runs excluded from the sandbox | works |
| `SOME_VAR=... uloop ...` (env-var prefix) | yes (verified empirically) | works |
| `ULOOP_PROJECT_RUNNER_PATH=<dist runner> uloop ...` (or `export` first, then plain `uloop ...`) | yes — the command text still starts with `uloop` | works |
| `dist/darwin-arm64/uloop compile ...` | **no** (verified even in the plain form with no `$(...)` substitution) | EPERM |
| raw `socket.connect()` from a script | no | EPERM |

The exclusion is decided on the **typed command text**, not on which binary ultimately does the
work: the `ULOOP_PROJECT_RUNNER_PATH` row runs a locally built dev runner yet stays excluded,
while the `dist/...` row is the same dispatcher code yet gets sandboxed. Game projects invoke
plain `uloop ...` (optionally with the env override) and never notice the sandbox. This
repository's development rule (see `CLAUDE.md` — always validate with the built
`dist/<platform>/uloop` binary) produces exactly the command shape the exclusion does
**not** match.

Note the corollary: a success for `uloop ...` in a sandboxed session does not mean the sandbox
permits Unity IPC — it means the command was excluded from sandboxing entirely.

## Remedies

Pick one:

1. Run dev-binary commands with the sandbox disabled for that command (Claude Code:
   `dangerouslyDisableSandbox`; users can manage restrictions via `/sandbox`).
2. Add the dev-binary shape to `excludedCommands` in the personal Claude Code settings, e.g.
   `"dist/*/uloop *"`, alongside the existing `"uloop *"`.

Do not burn time re-investigating the Editor side while the error is EPERM: the Editor never
saw the connection attempt.
