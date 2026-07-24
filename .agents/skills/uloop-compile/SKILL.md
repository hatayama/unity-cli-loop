---
name: uloop-compile
description: "Compile the Unity project and report errors/warnings. Use after C# edits or when a full Domain Reload compile is needed."
---

# uloop compile

Execute Unity project compilation.

## Usage

```bash
uloop compile [--force-recompile <true|false>] [--wait-for-domain-reload <true|false>]
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--force-recompile` | boolean value | `false` | Force full recompilation (triggers Domain Reload). Rarely needed — see "When to use --force-recompile" below. Pass `true` or `false`; bare flags are not accepted. |
| `--wait-for-domain-reload` | boolean value | `false` | Wait until Domain Reload completes before returning. Pass `true` or `false`; bare flags are not accepted. |

## When to use --force-recompile

`--force-recompile true` is almost never needed. Detecting changed files is Unity's job: even when
files were edited outside the Editor, a plain `uloop compile` refreshes assets and runs every
recompilation the changes require. "The files were changed externally, so recompile everything
just in case" is not a valid reason.

Why to avoid it:

- On large projects a full recompile plus Domain Reload can freeze Unity for a long time.
- With `--wait-for-domain-reload true`, the response crosses a Domain Reload and Unity cannot
  summarize compiler messages until after reload, so `Success` comes back as `null` and it does
  not work as a verification step.
- It puts the Editor into the unstable just-after-reload state for no benefit.

The one legitimate use case: you need warnings hidden by other asmdefs surfaced by a full
build. Otherwise always run plain `uloop compile`.

## Global Options

| Option | Description |
|--------|-------------|
| `--project-path <path>` | Optional. Use only when the target Unity project is not the current directory. |

## Examples

```bash
# Check compilation
uloop compile

# Force full recompilation
uloop compile --force-recompile true

# Force recompilation and wait for Domain Reload completion
uloop compile --force-recompile true --wait-for-domain-reload true

# Wait for Domain Reload completion even without force recompilation
uloop compile --force-recompile false --wait-for-domain-reload true
```

## Output

Returns JSON:
- `Success`: boolean
- `ErrorCount`: number
- `WarningCount`: number

## Troubleshooting

Diagnose the failure mode before retrying.

**Stale lock files** (CLI hangs or shows "Unity is busy" while Unity Editor *is* running):

```bash
uloop fix
```

This removes any leftover lock files (`compiling.lock`, `domainreload.lock`, `serverstarting.lock`) from the Unity project's Temp directory. Then retry `uloop compile`.

**Unity Editor not running** (CLI returns a connection failure and no Unity process is alive):

```bash
uloop launch
```

`uloop launch` auto-detects the project at the current working directory and opens it in the matching Unity Editor version. After Unity finishes launching, retry `uloop compile`.
