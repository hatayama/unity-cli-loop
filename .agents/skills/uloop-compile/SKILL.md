---
name: uloop-compile
toolName: compile
description: "Compile the Unity project and report errors/warnings. Use after C# edits."
---

# uloop compile

Execute Unity project compilation.

## Usage

```bash
uloop compile [--force-recompile] [--no-wait-for-domain-reload] [--stop-on-external-scene-changes] [--compile-wait-timeout-seconds <seconds>]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--force-recompile` | flag | - | Full recompile plus domain reload. Almost never needed: a plain compile already detects externally edited files, and the forced reload can freeze large projects and come back as `COMPILE_RESULT_UNKNOWN`. |
| `--no-wait-for-domain-reload` | flag | - | Return before Domain Reload completion |
| `--stop-on-external-scene-changes` | flag | - | Stop before compilation if open Scene files changed externally instead of auto-reloading them |
| `--compile-wait-timeout-seconds` | integer | 600 | Maximum seconds the CLI waits for the compile to finish before returning COMPILE_WAIT_TIMEOUT (default 600). Values above 1200 exceed the Unity-side result retention window (20 minutes) and weaken post-timeout recovery. |

## When to use --force-recompile

`--force-recompile` is almost never needed. Detecting changed files is Unity's job: even when
files were edited outside the Editor, a plain `uloop compile` refreshes assets and runs every
recompilation the changes require. "The files were changed externally, so recompile everything
just in case" is not a valid reason.

Why to avoid it:

- On large projects a full recompile plus domain reload can freeze Unity for a long time.
- The result crosses a domain reload, so it often comes back as `COMPILE_RESULT_UNKNOWN` and
  does not work as a verification step.
- It puts the Editor into the unstable just-after-reload state for no benefit.

The one legitimate use case: you need warnings hidden by other asmdefs surfaced by a full
build. Otherwise always run plain `uloop compile`.

## Output

Returns JSON:

- `Success`: boolean or null
- `ErrorCount`: number or null
- `WarningCount`: number or null
- `Message`: string

## Troubleshooting

If compile times out or Unity stops responding to uloop while the Editor looks idle, check whether Unity is showing **API Update Required** / **Script Updating Consent**. Ask the user to choose Go Ahead or No — never auto-dismiss that modal. Interactive Editors have no public uloop/Unity API to suppress it.
