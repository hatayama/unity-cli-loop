---
description: Link this repository's native Go uloop CLI as the global uloop command
allowed-tools: Bash, Read, Grep
---

# Link Go CLI

Link this repository's in-development Go CLI as the global `uloop` command.

## Goal

Build and verify the native Go CLI under `Packages/src/GoCli~`, then point the `uloop` command on PATH at this checkout's `uloop-dispatcher`.

## Important Assumptions

- The target is `Packages/src/GoCli~`. Do not use the old `Packages/src/Cli~` path or `npm link`.
- The global entrypoint is `uloop-dispatcher`. Do not link `uloop-core` directly because it is the project-local implementation.
- If an unrelated existing `uloop` executable exists as a regular file, do not overwrite it. Ask the user first.
- Do not treat the link as complete without verification.

## Steps

### Step 1: Confirm the Repository Root

```bash
git rev-parse --show-toplevel
git status --short --branch
```

Confirm that `Packages/src/GoCli~/go.mod` and `scripts/check-go-cli.sh` exist.

### Step 2: Build and Verify the Go CLI

```bash
scripts/check-go-cli.sh
```

This script checks Go CLI formatting, vet, lint, tests, and checked-in dist validation. If it fails, do not continue to linking. Report the cause instead.

### Step 3: Choose the Dispatcher for the Current Platform

```bash
uname -s
uname -m
```

macOS mapping:

- `Darwin` + `arm64` or `aarch64` -> `Packages/src/GoCli~/dist/darwin-arm64/uloop-dispatcher`
- `Darwin` + `x86_64` or `amd64` -> `Packages/src/GoCli~/dist/darwin-amd64/uloop-dispatcher`

Confirm that the target dispatcher exists and is executable.

### Step 4: Choose the Global Bin Directory

Priority order:

1. Use `ULOOP_GLOBAL_BIN_DIR` when it is set.
2. If `command -v uloop` succeeds, use the directory that contains that `uloop`.
3. Use `$HOME/.npm-global/bin` when it is on PATH or already contains `uloop`.
4. Use `$HOME/.local/bin` when it is on PATH.

If none of these can be used, do not create a link. Report that the user should choose a bin directory that is on PATH.

### Step 5: Create the Symlink

Create the `uloop` symlink in the selected directory.

Safety rules:

- If the existing `uloop` is a symlink, show its current target before updating it with `ln -sfn`.
- If the existing `uloop` is a regular file or directory, do not overwrite it. Ask the user first.
- Only use `mkdir -p` for the selected global bin directory.

Example:

```bash
ln -sfn "$DISPATCHER_PATH" "$GLOBAL_BIN_DIR/uloop"
```

### Step 6: Verify the Link Result

```bash
which uloop
readlink "$(which uloop)"
uloop --version
uloop --help
```

Confirm that the `readlink` result points at this checkout's `Packages/src/GoCli~/dist/.../uloop-dispatcher`.

## Completion Report

Briefly report the following:

- Result of `which uloop`
- Symlink target
- `uloop --version`
- `scripts/check-go-cli.sh` result
- Whether there are git changes
