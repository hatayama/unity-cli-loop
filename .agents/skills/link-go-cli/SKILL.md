---
name: link-go-cli
description: Link this repository's native Go uloop CLI dispatcher as the global `uloop` command. Use when the user asks to link, relink, or verify the Go CLI from this checkout, especially for `Packages/src/GoCli~`, `uloop-dispatcher`, or replacing old `npm link` / `Packages/src/Cli~` workflows.
---

# Link Go CLI

## Overview

Link the current checkout's native Go CLI dispatcher so `uloop` on PATH resolves to `Packages/src/GoCli~/dist/.../uloop-dispatcher`. Do not link `uloop-core` directly; it is the project-local implementation.

## Workflow

### 1. Confirm Repository State

Run from the repository root and confirm the Go CLI layout exists:

```bash
git rev-parse --show-toplevel
git status --short --branch
test -f Packages/src/GoCli~/go.mod
test -x scripts/check-go-cli.sh
```

If the repository root is different from the visible cwd, switch to the actual root before continuing.

### 2. Verify the Go CLI

Run:

```bash
scripts/check-go-cli.sh
```

Do not continue to linking if formatting, vet, lint, or tests fail.

If the script fails only because checked-in native binaries are out of date, run `scripts/build-go-cli.sh`, then rerun `scripts/check-go-cli.sh`. Report any resulting git changes and commit them when this invocation modified the repo.

### 3. Select the Dispatcher

Use the current platform:

```bash
uname -s
uname -m
```

Use these checked-in dispatchers:

- `Darwin` + `arm64` or `aarch64`: `Packages/src/GoCli~/dist/darwin-arm64/uloop-dispatcher`
- `Darwin` + `x86_64` or `amd64`: `Packages/src/GoCli~/dist/darwin-amd64/uloop-dispatcher`
- `MINGW*`, `MSYS*`, `CYGWIN*`, or `Windows_NT` + `x86_64` or `amd64`: `Packages/src/GoCli~/dist/windows-amd64/uloop-dispatcher.exe`

If the current platform does not match one of these combinations, stop without linking and report that this repository does not include a checked-in dispatcher for the current platform.

Confirm the dispatcher exists and is executable before linking.

### 4. Select the Global Bin Directory

Use this priority order:

1. `ULOOP_GLOBAL_BIN_DIR`, if set.
2. The directory containing `command -v uloop`, if `uloop` already exists.
3. `$HOME/.npm-global/bin`, if it is on PATH or already contains `uloop`.
4. `$HOME/.local/bin`, if it is on PATH.

If none of these can be used, do not create a link. Ask the user which PATH directory should hold `uloop`.

### 5. Create or Update the Symlink

Safety rules:

- If the existing `uloop` is a symlink, show its current target before updating it with `ln -sfn`.
- If the existing `uloop` is a regular file or directory, do not overwrite it. Ask the user first.
- Only create the selected global bin directory with `mkdir -p`; do not create unrelated PATH directories.

Example:

```bash
ln -sfn "$DISPATCHER_PATH" "$GLOBAL_BIN_DIR/uloop"
```

### 6. Verify the Link

Run:

```bash
which uloop
readlink "$(which uloop)"
uloop --version
uloop --help
```

The `readlink` result must point at this checkout's `Packages/src/GoCli~/dist/.../uloop-dispatcher`.

## Completion Report

Report the `which uloop` path, symlink target, `uloop --version`, `scripts/check-go-cli.sh` result, and any git changes.
